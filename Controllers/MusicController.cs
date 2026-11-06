using dndhelper.Core;
using dndhelper.Services;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/campaigns/{campaignId}/music")]
    public class MusicController : ControllerBase
    {
        private const long MaxAudioBytes = 50 * 1024 * 1024;
        private const long MaxCoverBytes = 2 * 1024 * 1024;

        private const string FetcherDown = "SpotFetcher is not running on the server. Try again in a minute.";

        private readonly MusicService _music;
        private readonly SpotFetcherClient _fetcher;
        private readonly ILogger<MusicController> _logger;

        public MusicController(MusicService music, SpotFetcherClient fetcher, ILogger<MusicController> logger)
        {
            _music = music;
            _fetcher = fetcher;
            _logger = logger;
        }

        public record NameRequest(string Name);
        public record IdsRequest(List<string> Ids);
        public record HashRequest(string Hash, string? CollectionId);
        public record TrackRequest(string? Title, string? Artist);
        public record MoveRequest(string CollectionId);
        public record PlayCollectionRequest(string CollectionId, string? From);
        public record EnqueueRequest(List<string>? Songs, string? CollectionId);
        public record SongRequest(string Song);
        public record QueueEntryRequest(int Index, string Song);
        public record MoveQueuedRequest(int From, int To, string Song);
        public record SeekRequest(double Seconds);
        public record KindRequest(string Kind);
        public record OnRequest(bool On);
        public record RepeatRequest(string Mode);
        public record SettingsRequest(double? Crossfade, string? Easing, bool? SoftPause);
        public record VoteRequest(bool Yes);
        public record LayerRequest(double? Level, bool? On);
        public record AmbienceRequest(string? CollectionId);
        public record FetchResolveRequest(string Url);
        public record FetchSearchRequest(string Query);
        public record FetchTrackRequest(string Id, int Bitrate, string? Pick);

        [HttpGet("fetch/health")]
        public async Task<IActionResult> FetchHealth(string campaignId, CancellationToken ct)
        {
            await _music.EnsureDriverAsync(campaignId);
            using var res = await _fetcher.TrySendAsync(HttpMethod.Get, "/health", null, false, ct);
            if (res is not { IsSuccessStatusCode: true }) return Ok(new { available = false, ffmpeg = false });
            var health = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            return Ok(new { available = true, ffmpeg = health.TryGetProperty("ffmpeg", out var f) && f.ValueKind == JsonValueKind.True });
        }

        [HttpPost("fetch/resolve")]
        public Task<IActionResult> FetchResolve(string campaignId, FetchResolveRequest r, CancellationToken ct) =>
            RelayAsync(campaignId, "/resolve", new { url = r.Url }, ct);

        [HttpPost("fetch/search")]
        public Task<IActionResult> FetchSearch(string campaignId, FetchSearchRequest r, CancellationToken ct) =>
            RelayAsync(campaignId, "/search", new { query = r.Query }, ct);

        private async Task<IActionResult> RelayAsync(string campaignId, string path, object body, CancellationToken ct)
        {
            await _music.EnsureDriverAsync(campaignId);
            using var res = await _fetcher.TrySendAsync(HttpMethod.Post, path, body, false, ct);
            if (res == null) return StatusCode(503, new { error = FetcherDown });
            return new ContentResult { StatusCode = (int)res.StatusCode, ContentType = "application/json", Content = await res.Content.ReadAsStringAsync(ct) };
        }

        [HttpPost("fetch/track")]
        public async Task FetchTrack(string campaignId, FetchTrackRequest r, CancellationToken ct)
        {
            await _music.EnsureDriverAsync(campaignId);
            using var res = await _fetcher.TrySendAsync(HttpMethod.Post, "/fetch", new { id = r.Id, bitrate = r.Bitrate, pick = r.Pick, maxBytes = MaxAudioBytes }, true, ct);
            if (res is not { IsSuccessStatusCode: true })
            {
                Response.StatusCode = res == null ? 503 : (int)res.StatusCode;
                Response.ContentType = "application/json";
                if (res == null) await Response.WriteAsJsonAsync(new { error = FetcherDown }, ct);
                else await res.Content.CopyToAsync(Response.Body, ct);
                return;
            }

            Response.ContentType = "application/x-ndjson";
            Response.Headers.CacheControl = "no-store";
            using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(ct));
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length == 0) continue;
                if (JsonNode.Parse(line) is not JsonObject ready || ready["file"] is not JsonValue file)
                {
                    await SendLineAsync(line, ct);
                    continue;
                }

                await SendLineAsync(JsonSerializer.Serialize(new { stage = "saving" }), ct);
                try
                {
                    using var audio = await _fetcher.DownloadAsync(file.GetValue<string>(), MaxAudioBytes, ct);
                    using var cover = await CoverAsync(ready["cover"], ct);
                    var artists = ready["artists"] is JsonArray list ? string.Join(", ", list.Select(a => a?.GetValue<string>()).Where(a => !string.IsNullOrWhiteSpace(a))) : null;
                    var duration = ready["duration"] is JsonValue d ? d.GetValue<double>() : 0;
                    await _music.AddTrackAsync(campaignId, null, audio, ready["name"]?.GetValue<string>(), cover, ready["title"]?.GetValue<string>(), artists, duration);
                    await SendLineAsync(JsonSerializer.Serialize(new { done = true }), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var userFacing = ex is ArgumentException or UnauthorizedAccessException or ConcurrencyException or NotFoundException;
                    if (!userFacing) _logger.LogError(ex, "Could not store a fetched song in campaign {CampaignId}", campaignId);
                    await SendLineAsync(JsonSerializer.Serialize(new { error = userFacing ? ex.Message : "The song came down, but could not be added to the library." }), ct);
                }
                return;
            }
        }

        private async Task<MemoryStream?> CoverAsync(JsonNode? path, CancellationToken ct)
        {
            if (path is not JsonValue value) return null;
            try
            {
                return await _fetcher.DownloadAsync(value.GetValue<string>(), MaxCoverBytes, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return null;
            }
        }

        private async Task SendLineAsync(string line, CancellationToken ct)
        {
            await Response.WriteAsync(line + "\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        [HttpGet("now")]
        public IActionResult Now() => Ok(MusicRuntime.Now);

        [HttpGet("state")]
        public async Task<IActionResult> State(string campaignId) => Ok(await _music.GetStateAsync(campaignId));

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat(string campaignId)
        {
            await _music.HeartbeatAsync(campaignId);
            return NoContent();
        }

        [HttpGet("library")]
        public async Task<IActionResult> Library(string campaignId) => Ok(await _music.GetLibraryAsync(campaignId));

        [HttpPost("collections")]
        public async Task<IActionResult> CreateSituation(string campaignId, NameRequest r) => Ok(new { id = await _music.CreateSituationAsync(campaignId, r.Name) });

        [HttpPut("collections/{id}")]
        public async Task<IActionResult> RenameSituation(string campaignId, string id, NameRequest r) { await _music.RenameSituationAsync(campaignId, id, r.Name); return NoContent(); }

        [HttpDelete("collections/{id}")]
        public async Task<IActionResult> DeleteSituation(string campaignId, string id) { await _music.DeleteSituationAsync(campaignId, id); return NoContent(); }

        [HttpPut("collections/order")]
        public async Task<IActionResult> ReorderSituations(string campaignId, IdsRequest r) { await _music.ReorderSituationsAsync(campaignId, r.Ids); return NoContent(); }

        [HttpPut("collections/{id}/tracks/order")]
        public async Task<IActionResult> ReorderTracks(string campaignId, string id, IdsRequest r) { await _music.ReorderTracksAsync(campaignId, id, r.Ids); return NoContent(); }

        [HttpPut("collections/{id}/layers/order")]
        public async Task<IActionResult> ReorderLayers(string campaignId, string id, IdsRequest r) { await _music.ReorderLayersAsync(campaignId, id, r.Ids); return NoContent(); }

        [HttpPost("tracks/reuse")]
        public async Task<IActionResult> ReuseTrack(string campaignId, HashRequest r) => Ok(new { reused = await _music.ReuseTrackAsync(campaignId, r.CollectionId, r.Hash) });

        [HttpPost("tracks")]
        [RequestSizeLimit(MaxAudioBytes + MaxCoverBytes + 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioBytes + MaxCoverBytes + 1024 * 1024)]
        public async Task<IActionResult> AddTrack(string campaignId, IFormFile? file, IFormFile? cover,
            [FromForm] string? collectionId, [FromForm] string? title, [FromForm] string? artist, [FromForm] string? duration)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "Choose a song to upload." });
            if (file.Length > MaxAudioBytes) return BadRequest(new { message = "Songs can be at most 50 MB." });
            if (cover != null && cover.Length > MaxCoverBytes) return BadRequest(new { message = "That cover image is too big." });

            await using var audio = await BufferAsync(file);
            await using var art = cover != null ? await BufferAsync(cover) : null;
            var seconds = double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            await _music.AddTrackAsync(campaignId, collectionId, audio, file.FileName, art, title, artist, seconds);
            return NoContent();
        }

        [HttpPatch("tracks/{id}")]
        public async Task<IActionResult> UpdateTrack(string campaignId, string id, TrackRequest r) { await _music.UpdateTrackAsync(campaignId, id, r.Title, r.Artist); return NoContent(); }

        [HttpPut("tracks/{id}/cover")]
        [RequestSizeLimit(MaxCoverBytes + 1024 * 1024)]
        public async Task<IActionResult> SetCover(string campaignId, string id, IFormFile? cover)
        {
            if (cover == null || cover.Length == 0) return BadRequest(new { message = "Choose an image." });
            if (cover.Length > MaxCoverBytes) return BadRequest(new { message = "That cover image is too big." });
            await using var art = await BufferAsync(cover);
            await _music.SetTrackCoverAsync(campaignId, id, art);
            return NoContent();
        }

        [HttpDelete("tracks/{id}/cover")]
        public async Task<IActionResult> RemoveCover(string campaignId, string id) { await _music.SetTrackCoverAsync(campaignId, id, null); return NoContent(); }

        [HttpPut("tracks/{id}/collection")]
        public async Task<IActionResult> MoveTrack(string campaignId, string id, MoveRequest r) { await _music.MoveTrackToAsync(campaignId, id, r.CollectionId); return NoContent(); }

        [HttpDelete("tracks/{id}")]
        public async Task<IActionResult> DeleteTrack(string campaignId, string id) { await _music.DeleteTrackAsync(campaignId, id); return NoContent(); }

        [HttpPost("play-collection")]
        public async Task<IActionResult> PlayCollection(string campaignId, PlayCollectionRequest r) => Ok(await _music.PlayCollectionAsync(campaignId, r.CollectionId, r.From));

        [HttpPost("enqueue")]
        public async Task<IActionResult> Enqueue(string campaignId, EnqueueRequest r) => Ok(await _music.EnqueueAsync(campaignId, r.Songs, r.CollectionId));

        [HttpPost("play-now")]
        public async Task<IActionResult> PlayNow(string campaignId, SongRequest r) => Ok(await _music.PlayNowAsync(campaignId, r.Song));

        [HttpPost("jump")]
        public async Task<IActionResult> Jump(string campaignId, QueueEntryRequest r) => Ok(await _music.JumpAsync(campaignId, r.Index, r.Song));

        [HttpPost("move-queued")]
        public async Task<IActionResult> MoveQueued(string campaignId, MoveQueuedRequest r) => Ok(await _music.MoveQueuedAsync(campaignId, r.From, r.To, r.Song));

        [HttpPost("remove-queued")]
        public async Task<IActionResult> RemoveQueued(string campaignId, QueueEntryRequest r) => Ok(await _music.RemoveFromQueueAsync(campaignId, r.Index, r.Song));

        [HttpPost("clear")]
        public async Task<IActionResult> Clear(string campaignId) => Ok(await _music.ClearQueueAsync(campaignId));

        [HttpPost("play")]
        public async Task<IActionResult> Play(string campaignId) => Ok(await _music.PlayAsync(campaignId));

        [HttpPost("pause")]
        public async Task<IActionResult> Pause(string campaignId) => Ok(await _music.PauseAsync(campaignId));

        [HttpPost("seek")]
        public async Task<IActionResult> Seek(string campaignId, SeekRequest r) => Ok(await _music.SeekAsync(campaignId, r.Seconds));

        [HttpPost("skip")]
        public async Task<IActionResult> Skip(string campaignId, KindRequest r) => Ok(await _music.SkipAsync(campaignId, r.Kind));

        [HttpPost("shuffle")]
        public async Task<IActionResult> Shuffle(string campaignId, OnRequest r) => Ok(await _music.SetShuffleAsync(campaignId, r.On));

        [HttpPost("repeat")]
        public async Task<IActionResult> Repeat(string campaignId, RepeatRequest r) => Ok(await _music.SetRepeatAsync(campaignId, r.Mode));

        [HttpPut("settings")]
        public async Task<IActionResult> Settings(string campaignId, SettingsRequest r) => Ok(await _music.SetSettingsAsync(campaignId, r.Crossfade, r.Easing, r.SoftPause));

        [HttpPost("votes")]
        public async Task<IActionResult> RequestSkip(string campaignId, KindRequest r) => Ok(await _music.RequestSkipAsync(campaignId, r.Kind));

        [HttpPost("votes/{id}")]
        public async Task<IActionResult> Vote(string campaignId, string id, VoteRequest r) => Ok(await _music.VoteAsync(campaignId, id, r.Yes));

        [HttpPost("collections/{id}/layers")]
        [RequestSizeLimit(MaxAudioBytes + 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioBytes + 1024 * 1024)]
        public async Task<IActionResult> AddLayer(string campaignId, string id, IFormFile? file, [FromForm] string? name)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "Choose a sound to upload." });
            if (file.Length > MaxAudioBytes) return BadRequest(new { message = "Sounds can be at most 50 MB." });
            await using var audio = await BufferAsync(file);
            await _music.AddLayerAsync(campaignId, id, audio, file.FileName, name);
            return NoContent();
        }

        [HttpPut("layers/{id}/name")]
        public async Task<IActionResult> RenameLayer(string campaignId, string id, NameRequest r) { await _music.RenameLayerAsync(campaignId, id, r.Name); return NoContent(); }

        [HttpPatch("layers/{id}")]
        public async Task<IActionResult> SetLayer(string campaignId, string id, LayerRequest r) { await _music.SetLayerAsync(campaignId, id, r.Level, r.On); return NoContent(); }

        [HttpPut("layers/{id}/collection")]
        public async Task<IActionResult> MoveLayer(string campaignId, string id, MoveRequest r) { await _music.MoveLayerToAsync(campaignId, id, r.CollectionId); return NoContent(); }

        [HttpDelete("layers/{id}")]
        public async Task<IActionResult> DeleteLayer(string campaignId, string id) { await _music.DeleteLayerAsync(campaignId, id); return NoContent(); }

        [HttpPut("ambience")]
        public async Task<IActionResult> PlayAmbience(string campaignId, AmbienceRequest r) { await _music.PlayAmbienceAsync(campaignId, r.CollectionId); return NoContent(); }

        private static async Task<MemoryStream> BufferAsync(IFormFile file)
        {
            var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            buffer.Position = 0;
            return buffer;
        }
    }
}
