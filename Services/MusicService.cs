using dndhelper.Authorization;
using dndhelper.Core;
using dndhelper.Models;
using dndhelper.Repositories;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class MusicService
    {
        private const long VoteMs = 30_000;
        private const long EndGraceMs = 250;
        private const long LeadMs = 1000;
        private const long FollowOnLeadMs = 500;
        private const double MaxCrossfade = 12;
        private const double MaxDuration = 4 * 3600;
        private const int NameMax = 60;
        private const string InboxName = "Unsorted";
        private static readonly string[] RepeatModes = { "off", "all", "one" };
        private static readonly string[] Easings = { "inOut", "in", "out" };

        private readonly MusicRepository _db;
        private readonly MusicRuntime _runtime;
        private readonly ICampaignRepository _campaigns;
        private readonly CampaignAccess _access;
        private readonly IEntitySyncService _sync;
        private readonly IFileStorageService _files;
        private readonly IMemoryCache _cache;
        private readonly ILogger _logger;
        private readonly Dictionary<string, MusicTrack?> _trackMemo = new();

        public MusicService(MusicRepository db, MusicRuntime runtime, ICampaignRepository campaigns, CampaignAccess access,
            IEntitySyncService sync, IFileStorageService files, IMemoryCache cache, ILogger logger)
        {
            _db = db;
            _runtime = runtime;
            _campaigns = campaigns;
            _access = access;
            _sync = sync;
            _files = files;
            _cache = cache;
            _logger = logger;
        }

        private static long Now => MusicRuntime.Now;
        private string Me => _access.UserId ?? throw new ForbiddenException("Sign in first.");

        private bool Drives(Campaign c) => _access.IsSuperAdmin || c.CanDriveMusic(_access.UserId);

        public Task EnsureDriverAsync(string campaignId) => DriverAsync(campaignId);

        private async Task<Campaign> DriverAsync(string campaignId)
        {
            var c = await _access.EnsureMemberAsync(campaignId);
            if (!Drives(c)) throw new ForbiddenException("Only the bard or the DM can do that.");
            return c;
        }

        private sealed class Change
        {
            public List<string>? Queue;
            public List<string>? Order;
            public int? Index;
            public bool? Playing;
            public long? StartedAt;
            public double? Position;
            public bool? Shuffle;
            public string? Repeat;
        }

        private static Change FromTop(int index) => new() { Index = index, Position = 0, StartedAt = Now };

        private static string? UpNext(MusicPlayer p)
        {
            if (p.Repeat == "one") return null;
            if (p.Index + 1 < p.Queue.Count) return p.Queue[p.Index + 1];
            return p.Repeat == "all" && !p.Shuffle && p.Queue.Count > 0 ? p.Queue[0] : null;
        }

        private static List<string> Shuffled(IEnumerable<string> items)
        {
            var a = items.ToList();
            for (var i = a.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
            return a;
        }

        private static Change? Step(MusicPlayer p, int dir)
        {
            var n = p.Queue.Count;
            if (n == 0) return null;
            var i = p.Index + dir;
            if (i >= n)
            {
                if (p.Repeat == "off") return null;
                if (!p.Shuffle || n < 2) return FromTop(0);
                var queue = Shuffled(p.Queue);
                if (queue[0] == p.Queue[p.Index]) (queue[0], queue[n - 1]) = (queue[n - 1], queue[0]);
                var round = FromTop(0);
                round.Queue = queue;
                return round;
            }
            if (i < 0) return p.Repeat == "all" ? FromTop(n - 1) : FromTop(0);
            return FromTop(i);
        }

        public static (List<string> Queue, int Index) MoveInQueue(List<string> queue, int index, int from, int to)
        {
            var next = queue.ToList();
            var item = next[from];
            next.RemoveAt(from);
            next.Insert(to, item);
            if (from == index) return (next, to);
            if (from < index && to >= index) return (next, index - 1);
            if (from > index && to <= index) return (next, index + 1);
            return (next, index);
        }

        private static long? EndsAt(MusicPlayer s, MusicTrack? track)
        {
            if (track == null || !s.Playing) return null;
            var end = s.StartedAt + (long)(track.Duration * 1000);
            var goesOn = s.Repeat == "all" || (s.Repeat == "off" && s.Index < s.Queue.Count - 1);
            var fade = goesOn ? Math.Min(s.Crossfade, track.Duration / 3) : 0;
            return fade > 0 ? end - (long)(fade * 1000) - FollowOnLeadMs : end + EndGraceMs;
        }

        private async Task<MusicTrack?> TrackAsync(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!_trackMemo.TryGetValue(id, out var t))
                _trackMemo[id] = t = await _db.Tracks.Find(x => x.Id == id).FirstOrDefaultAsync();
            return t;
        }

        private async Task<MusicTrack?> CurrentAsync(MusicPlayer p) =>
            p.Queue.Count > 0 ? await TrackAsync(p.Queue[Math.Clamp(p.Index, 0, p.Queue.Count - 1)]) : null;

        private async Task ArmAsync(MusicPlayer p)
        {
            var end = EndsAt(p, await CurrentAsync(p));
            var vote = p.Vote?.Status == "open" ? p.Vote.EndsAt : (long?)null;
            _runtime.WakeAt(p.CampaignId!, end == null ? vote : vote == null ? end : Math.Min(end.Value, vote.Value));
        }

        private async Task CommitAsync(MusicPlayer p, Change c, bool followOn = false)
        {
            var wasTrack = p.Queue.ElementAtOrDefault(p.Index);
            var wasIndex = p.Index;
            var upNext = UpNext(p);

            var queue = c.Queue ?? p.Queue;
            var index = c.Index ?? p.Index;
            var playing = c.Playing ?? p.Playing;
            var startedAt = c.StartedAt ?? p.StartedAt;
            var position = c.Position ?? p.Position;

            if (queue.Count == 0) { index = 0; playing = false; position = 0; }
            else index = Math.Clamp(index, 0, queue.Count - 1);
            var track = queue.Count > 0 ? await TrackAsync(queue[index]) : null;
            if (track == null) playing = false;
            if (track != null && !playing) position = Math.Clamp(position, 0, track.Duration);
            var newSong = track != null && (track.Id != wasTrack || index != wasIndex);
            if (newSong && playing && c.StartedAt != null)
            {
                var lead = followOn && track!.Id == upNext ? FollowOnLeadMs : LeadMs;
                startedAt = Math.Max(startedAt, Now + lead);
            }

            p.Queue = queue;
            p.Order = c.Order ?? p.Order;
            p.Index = index;
            p.Playing = playing;
            p.StartedAt = startedAt;
            p.Position = position;
            p.Shuffle = c.Shuffle ?? p.Shuffle;
            p.Repeat = c.Repeat ?? p.Repeat;
            p.Rev++;

            if ((track?.Id != wasTrack || index != wasIndex) && p.Vote?.Status == "open")
            {
                p.Vote.Status = "cancelled";
                p.Vote.ClosedAt = Now;
            }
        }

        private async Task SongEndedAsync(MusicPlayer p)
        {
            if (p.Repeat == "one") await CommitAsync(p, FromTop(p.Index));
            else await CommitAsync(p, Step(p, 1) ?? new Change { Playing = false, Position = 0 }, followOn: true);
        }

        private async Task CloseVoteAsync(MusicPlayer p, bool passed, string decidedBy)
        {
            var vote = p.Vote!;
            vote.ClosedAt = Now;
            var still = p.Queue.ElementAtOrDefault(p.Index) == vote.Track && p.Index == vote.Index;
            if (!still)
            {
                vote.Status = "cancelled";
                return;
            }
            vote.Status = passed ? "passed" : "failed";
            vote.DecidedBy = decidedBy;
            var change = passed ? Step(p, vote.Kind == "next" ? 1 : -1) : null;
            if (change != null) await CommitAsync(p, change);
        }

        private async Task TallyAsync(MusicPlayer p)
        {
            var v = p.Vote!;
            if (v.Yes.Count * 2 > v.Eligible) await CloseVoteAsync(p, true, "vote");
            else if (v.No.Count * 2 >= v.Eligible) await CloseVoteAsync(p, false, "vote");
        }

        private async Task<bool> CatchUpAsync(MusicPlayer p)
        {
            var changed = false;
            if (p.Vote is { Status: "open" } vote && Now >= vote.EndsAt)
            {
                await CloseVoteAsync(p, vote.Yes.Count > vote.No.Count, "time");
                changed = true;
            }
            var end = EndsAt(p, await CurrentAsync(p));
            if (end != null && Now >= end)
            {
                await SongEndedAsync(p);
                changed = true;
            }
            return changed;
        }

        private async Task<MusicPlayer> LoadPlayerAsync(string campaignId)
        {
            var p = await _db.Players.Find(x => x.CampaignId == campaignId).FirstOrDefaultAsync();
            if (p != null) return p;
            p = new MusicPlayer { CampaignId = campaignId };
            await _db.Players.InsertOneAsync(p);
            return p;
        }

        private Task SaveAsync(MusicPlayer p) => _db.Players.ReplaceOneAsync(x => x.Id == p.Id, p);

        public async Task TickAsync(string campaignId)
        {
            using (await _runtime.LockAsync(campaignId))
            {
                var p = await LoadPlayerAsync(campaignId);
                if (await CatchUpAsync(p)) await SaveAsync(p);
                await ArmAsync(p);
                var campaign = await _campaigns.GetByIdAsync(campaignId);
                if (campaign != null) await PushStateAsync(campaign, p);
            }
        }

        private async Task<object> PlayerOpAsync(string campaignId, bool driver, Func<Campaign, MusicPlayer, Task> op)
        {
            var campaign = driver ? await DriverAsync(campaignId) : await _access.EnsureMemberAsync(campaignId);
            using (await _runtime.LockAsync(campaignId))
            {
                var p = await LoadPlayerAsync(campaignId);
                await CatchUpAsync(p);
                await op(campaign, p);
                await SaveAsync(p);
                await ArmAsync(p);
                return await PushStateAsync(campaign, p);
            }
        }

        private async Task LibraryOpAsync(string campaignId, Func<Campaign, MusicPlayer, Task> op)
        {
            var campaign = await DriverAsync(campaignId);
            using (await _runtime.LockAsync(campaignId))
            {
                var p = await LoadPlayerAsync(campaignId);
                var rev = p.Rev;
                await op(campaign, p);
                await SaveAsync(p);
                if (p.Rev != rev) await ArmAsync(p);
                await PushStateAsync(campaign, p);
                await PushLibraryAsync(campaign);
            }
        }

        private async Task<string?> UrlAsync(string? fileId)
        {
            if (string.IsNullOrEmpty(fileId)) return null;
            var key = $"music-url:{fileId}";
            if (_cache.TryGetValue(key, out string? url)) return url;
            var file = await _files.GetAsync(fileId);
            if (file == null) return null;
            url = file.Url ?? await _files.GetSignedUrlAsync(file, TimeSpan.FromDays(7));
            _cache.Set(key, url, TimeSpan.FromDays(file.Url != null ? 30 : 6));
            return url;
        }

        private async Task<Dictionary<string, string>> CollectionNamesAsync(string campaignId) =>
            (await _db.Collections.Find(c => c.CampaignId == campaignId).ToListAsync())
                .ToDictionary(c => c.Id!, c => c.Kind == MusicKinds.Inbox ? InboxName : c.Name);

        private async Task<object> StateAsync(MusicPlayer p)
        {
            var ids = p.Queue.Distinct().ToList();
            var tracks = (await _db.Tracks.Find(t => t.CampaignId == p.CampaignId && ids.Contains(t.Id!)).ToListAsync()).ToDictionary(t => t.Id!);
            var names = await CollectionNamesAsync(p.CampaignId!);

            var queue = new List<object>();
            foreach (var id in p.Queue)
            {
                if (!tracks.TryGetValue(id, out var t)) continue;
                queue.Add(new { id = t.Id, title = t.Title, artist = t.Artist, cover = await UrlAsync(t.CoverFileId), duration = t.Duration, collection = names.GetValueOrDefault(t.CollectionId, "") });
            }

            object? track = null;
            if (p.Queue.Count > 0 && tracks.TryGetValue(p.Queue[p.Index], out var cur) && await UrlAsync(cur.FileId) is { } url)
                track = new { id = cur.Id, title = cur.Title, artist = cur.Artist, cover = await UrlAsync(cur.CoverFileId), duration = cur.Duration, collection = names.GetValueOrDefault(cur.CollectionId, ""), url };
            var next = UpNext(p);
            var nextUrl = next != null && tracks.TryGetValue(next, out var nt) ? await UrlAsync(nt.FileId) : null;

            object? ambience = null;
            if (p.AmbienceCollectionId != null)
            {
                var layers = new List<object>();
                foreach (var l in (await _db.Layers.Find(l => l.CampaignId == p.CampaignId && l.CollectionId == p.AmbienceCollectionId && l.On).ToListAsync()).OrderBy(l => l.Order))
                    if (await UrlAsync(l.FileId) is { } layerUrl)
                        layers.Add(new { id = l.Id, name = l.Name, level = l.Level, url = layerUrl });
                if (layers.Count > 0)
                    ambience = new { collection = p.AmbienceCollectionId, name = names.GetValueOrDefault(p.AmbienceCollectionId, ""), layers };
            }

            return new
            {
                rev = p.Rev,
                fade = new { crossfade = p.Crossfade, easing = p.Easing, softPause = p.SoftPause },
                ambience,
                track,
                nextUrl,
                index = p.Index,
                queue,
                playing = p.Playing,
                startedAt = p.StartedAt,
                position = p.Position,
                shuffle = p.Shuffle,
                repeat = p.Repeat,
                vote = p.Vote,
            };
        }

        private async Task<object> PushStateAsync(Campaign campaign, MusicPlayer p)
        {
            var state = await StateAsync(p);
            await _sync.BroadcastToUsers("MusicState", new { campaignId = campaign.Id, state }, campaign.Members.Select(m => m.UserId).ToList());
            return state;
        }

        private Task PushLibraryAsync(Campaign campaign) =>
            _sync.BroadcastToUsers("MusicLibraryChanged", new { campaignId = campaign.Id },
                campaign.Members.Where(m => campaign.CanDriveMusic(m.UserId)).Select(m => m.UserId).ToList());

        public async Task<object> GetStateAsync(string campaignId)
        {
            await _access.EnsureMemberAsync(campaignId);
            using (await _runtime.LockAsync(campaignId))
            {
                var p = await LoadPlayerAsync(campaignId);
                if (await CatchUpAsync(p)) await SaveAsync(p);
                await ArmAsync(p);
                return await StateAsync(p);
            }
        }

        public async Task HeartbeatAsync(string campaignId)
        {
            await _access.EnsureMemberAsync(campaignId);
            _runtime.Here(campaignId, Me);
        }

        public async Task<object> GetLibraryAsync(string campaignId)
        {
            await DriverAsync(campaignId);
            var p = await _db.Players.Find(x => x.CampaignId == campaignId).FirstOrDefaultAsync();
            var collections = await _db.Collections.Find(c => c.CampaignId == campaignId).ToListAsync();
            var tracks = await _db.Tracks.Find(t => t.CampaignId == campaignId).ToListAsync();
            var layers = await _db.Layers.Find(l => l.CampaignId == campaignId).ToListAsync();

            var songs = new Dictionary<string, object>();
            foreach (var t in tracks)
                songs[t.Id!] = new { id = t.Id, title = t.Title, artist = t.Artist, cover = await UrlAsync(t.CoverFileId), url = await UrlAsync(t.FileId), duration = t.Duration };
            var layerViews = new Dictionary<string, object>();
            foreach (var l in layers)
                layerViews[l.Id!] = new { id = l.Id, name = l.Name, level = l.Level, on = l.On, url = await UrlAsync(l.FileId) };

            List<object> SongsOf(string id) => tracks.Where(t => t.CollectionId == id).OrderBy(t => t.Order).Select(t => songs[t.Id!]).ToList();
            List<object> LayersOf(string id) => layers.Where(l => l.CollectionId == id).OrderBy(l => l.Order).Select(l => layerViews[l.Id!]).ToList();

            var inbox = collections.FirstOrDefault(c => c.Kind == MusicKinds.Inbox);
            return new
            {
                inbox = new { id = inbox?.Id, name = InboxName, songs = inbox != null ? SongsOf(inbox.Id!) : new List<object>() },
                settings = new { crossfade = p?.Crossfade ?? 0, easing = p?.Easing ?? "inOut", softPause = p?.SoftPause ?? false },
                ambience = p?.AmbienceCollectionId,
                situations = collections.Where(c => c.Kind == MusicKinds.Situation).OrderBy(c => c.Order)
                    .Select(c => new { id = c.Id, name = c.Name, songs = SongsOf(c.Id!), layers = LayersOf(c.Id!) }),
            };
        }

        private async Task<MusicCollection> CollectionAsync(string campaignId, string? id)
        {
            if (string.IsNullOrEmpty(id)) return await InboxAsync(campaignId);
            return await _db.Collections.Find(c => c.Id == id && c.CampaignId == campaignId).FirstOrDefaultAsync()
                ?? throw new NotFoundException("That collection no longer exists.");
        }

        private async Task<MusicCollection> InboxAsync(string campaignId)
        {
            var inbox = await _db.Collections.Find(c => c.CampaignId == campaignId && c.Kind == MusicKinds.Inbox).FirstOrDefaultAsync();
            if (inbox != null) return inbox;
            inbox = new MusicCollection { CampaignId = campaignId, Kind = MusicKinds.Inbox, Name = InboxName };
            await _db.Collections.InsertOneAsync(inbox);
            return inbox;
        }

        private async Task<MusicTrack> OwnTrackAsync(string campaignId, string id) =>
            await _db.Tracks.Find(t => t.Id == id && t.CampaignId == campaignId).FirstOrDefaultAsync()
                ?? throw new NotFoundException("That song no longer exists.");

        private async Task<MusicTrack> OwnTrackByIdAsync(string campaignId, string id)
        {
            var t = await TrackAsync(id);
            return t != null && t.CampaignId == campaignId ? t : throw new NotFoundException("A song in that list no longer exists.");
        }

        private static string CleanName(string? name, string error)
        {
            var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (clean.Length == 0) throw new ArgumentException(error);
            return clean.Length > NameMax ? clean[..NameMax] : clean;
        }

        private static void SameSet(List<string> ids, IEnumerable<string> existing, string error)
        {
            var have = existing.ToList();
            if (ids.Count != have.Count || ids.Distinct().Count() != ids.Count || !ids.All(have.Contains))
                throw new ConcurrencyException(error);
        }

        private async Task<int> NextTrackOrderAsync(string collectionId) =>
            (await _db.Tracks.Find(t => t.CollectionId == collectionId).SortByDescending(t => t.Order).FirstOrDefaultAsync())?.Order + 1 ?? 1;

        private async Task ReleaseAsync(string campaignId, IEnumerable<string?> fileIds)
        {
            foreach (var id in fileIds.Where(f => !string.IsNullOrEmpty(f)).Distinct())
            {
                var used = await _db.Tracks.Find(t => t.CampaignId == campaignId && (t.FileId == id || t.CoverFileId == id)).AnyAsync()
                    || await _db.Layers.Find(l => l.CampaignId == campaignId && l.FileId == id).AnyAsync();
                if (used) continue;
                try
                {
                    await _files.DeleteAsync(id!);
                    _cache.Remove($"music-url:{id}");
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not remove music file {FileId}", id);
                }
            }
        }

        private async Task DropFromQueueAsync(MusicPlayer p, HashSet<string> ids)
        {
            if (!p.Queue.Any(ids.Contains)) return;
            var currentGoes = ids.Contains(p.Queue[p.Index]);
            var before = p.Queue.Take(p.Index).Count(id => !ids.Contains(id));
            var queue = p.Queue.Where(id => !ids.Contains(id)).ToList();
            var order = p.Order.Where(id => !ids.Contains(id)).ToList();
            var change = currentGoes ? FromTop(before) : new Change { Index = before };
            change.Queue = queue;
            change.Order = order;
            await CommitAsync(p, change);
        }

        public async Task<string> CreateSituationAsync(string campaignId, string name)
        {
            var created = new MusicCollection { CampaignId = campaignId, Kind = MusicKinds.Situation, Name = CleanName(name, "Give the collection a name.") };
            await LibraryOpAsync(campaignId, async (_, _) =>
            {
                var last = await _db.Collections.Find(c => c.CampaignId == campaignId).SortByDescending(c => c.Order).FirstOrDefaultAsync();
                created.Order = (last?.Order ?? 0) + 1;
                await _db.Collections.InsertOneAsync(created);
            });
            return created.Id!;
        }

        public Task RenameSituationAsync(string campaignId, string id, string name) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var clean = CleanName(name, "Give the collection a name.");
                var c = await CollectionAsync(campaignId, id);
                if (c.Kind != MusicKinds.Situation) throw new ArgumentException("Only your own collections can be renamed.");
                await _db.Collections.UpdateOneAsync(x => x.Id == id, Builders<MusicCollection>.Update.Set(x => x.Name, clean));
            });

        public Task DeleteSituationAsync(string campaignId, string id) =>
            LibraryOpAsync(campaignId, async (_, p) =>
            {
                var c = await CollectionAsync(campaignId, id);
                if (c.Kind != MusicKinds.Situation) throw new ArgumentException("Unsorted can't be deleted.");
                var tracks = await _db.Tracks.Find(t => t.CollectionId == id).ToListAsync();
                var layers = await _db.Layers.Find(l => l.CollectionId == id).ToListAsync();
                await DropFromQueueAsync(p, tracks.Select(t => t.Id!).ToHashSet());
                await _db.Tracks.DeleteManyAsync(t => t.CollectionId == id);
                await _db.Layers.DeleteManyAsync(l => l.CollectionId == id);
                await _db.Collections.DeleteOneAsync(x => x.Id == id);
                if (p.AmbienceCollectionId == id) p.AmbienceCollectionId = null;
                await ReleaseAsync(campaignId, tracks.SelectMany(t => new[] { t.FileId, t.CoverFileId }).Concat(layers.Select(l => l.FileId)));
            });

        public Task ReorderSituationsAsync(string campaignId, List<string> ids) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var situations = await _db.Collections.Find(c => c.CampaignId == campaignId && c.Kind == MusicKinds.Situation).ToListAsync();
                SameSet(ids, situations.Select(c => c.Id!), "The collections changed. Try again.");
                for (var i = 0; i < ids.Count; i++)
                    await _db.Collections.UpdateOneAsync(c => c.Id == ids[i], Builders<MusicCollection>.Update.Set(c => c.Order, i + 1));
            });

        public async Task<bool> ReuseTrackAsync(string campaignId, string? collectionId, string hash)
        {
            if (!IsHash(hash)) throw new ArgumentException("The browser could not read that song.");
            var reused = false;
            await LibraryOpAsync(campaignId, async (_, _) =>
            {
                var same = await _db.Tracks.Find(t => t.CampaignId == campaignId && t.Hash == hash).FirstOrDefaultAsync();
                if (same == null) return;
                var collection = await CollectionAsync(campaignId, collectionId);
                await InsertCopyAsync(collection, same);
                reused = true;
            });
            return reused;
        }

        private async Task InsertCopyAsync(MusicCollection collection, MusicTrack same)
        {
            var here = await _db.Tracks.Find(t => t.CollectionId == collection.Id && (t.Hash == same.Hash || t.FileId == same.FileId)).FirstOrDefaultAsync();
            if (here != null) throw new AlreadyExistsException($"“{here.Title}” is already in this collection.");
            await _db.Tracks.InsertOneAsync(new MusicTrack
            {
                CampaignId = collection.CampaignId,
                CollectionId = collection.Id!,
                FileId = same.FileId,
                CoverFileId = same.CoverFileId,
                Title = same.Title,
                Artist = same.Artist,
                Hash = same.Hash,
                Duration = same.Duration,
                Order = await NextTrackOrderAsync(collection.Id!),
            });
        }

        private static bool IsHash(string? hash) => hash != null && hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

        public async Task AddTrackAsync(string campaignId, string? collectionId, Stream audio, string? fileName, Stream? cover,
            string? title, string? artist, double duration)
        {
            if (!(duration > 0 && duration < MaxDuration)) throw new ArgumentException("The browser could not read how long that song is.");
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(audio)).ToLowerInvariant();
            audio.Position = 0;

            await LibraryOpAsync(campaignId, async (_, _) =>
            {
                var collection = await CollectionAsync(campaignId, collectionId);
                var same = await _db.Tracks.Find(t => t.CampaignId == campaignId && t.Hash == hash).FirstOrDefaultAsync();
                if (same != null)
                {
                    await InsertCopyAsync(collection, same);
                    return;
                }

                var file = await _files.UploadAsync(audio, StorageFolder.Music, Me, fileName);
                StoredFile? coverFile = null;
                try
                {
                    if (cover != null) coverFile = await _files.UploadAsync(cover, StorageFolder.Images, Me, "cover.jpg");
                    await _db.Tracks.InsertOneAsync(new MusicTrack
                    {
                        CampaignId = campaignId,
                        CollectionId = collection.Id!,
                        FileId = file.Id!,
                        CoverFileId = coverFile?.Id,
                        Title = Clip(title, 200) ?? Clip(Path.GetFileNameWithoutExtension(fileName ?? ""), 200) ?? "Untitled",
                        Artist = Clip(artist, 200),
                        Hash = hash,
                        Duration = duration,
                        Order = await NextTrackOrderAsync(collection.Id!),
                    });
                }
                catch
                {
                    await _files.DeleteAsync(file.Id!);
                    if (coverFile != null) await _files.DeleteAsync(coverFile.Id!);
                    throw;
                }
            });
        }

        private static string? Clip(string? s, int max)
        {
            var t = s?.Trim();
            return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
        }

        public Task UpdateTrackAsync(string campaignId, string id, string? title, string? artist) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                await OwnTrackAsync(campaignId, id);
                var update = new List<UpdateDefinition<MusicTrack>>();
                if (title != null)
                    update.Add(Builders<MusicTrack>.Update.Set(t => t.Title, Clip(title, 200) ?? throw new ArgumentException("A song needs a title.")));
                if (artist != null) update.Add(Builders<MusicTrack>.Update.Set(t => t.Artist, Clip(artist, 200)));
                if (update.Count > 0) await _db.Tracks.UpdateOneAsync(t => t.Id == id, Builders<MusicTrack>.Update.Combine(update));
            });

        public Task SetTrackCoverAsync(string campaignId, string id, Stream? cover) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var t = await OwnTrackAsync(campaignId, id);
                var file = cover != null ? await _files.UploadAsync(cover, StorageFolder.Images, Me, "cover.jpg") : null;
                await _db.Tracks.UpdateOneAsync(x => x.Id == id, Builders<MusicTrack>.Update.Set(x => x.CoverFileId, file?.Id));
                await ReleaseAsync(campaignId, new[] { t.CoverFileId });
            });

        public Task MoveTrackToAsync(string campaignId, string id, string collectionId) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var t = await OwnTrackAsync(campaignId, id);
                if (t.CollectionId == collectionId) return;
                var target = await CollectionAsync(campaignId, collectionId);
                var there = await _db.Tracks.Find(x => x.CollectionId == target.Id && (x.FileId == t.FileId || x.Hash == t.Hash)).AnyAsync();
                if (there) throw new AlreadyExistsException($"“{t.Title}” is already in that collection.");
                await _db.Tracks.UpdateOneAsync(x => x.Id == id, Builders<MusicTrack>.Update
                    .Set(x => x.CollectionId, target.Id!)
                    .Set(x => x.Order, await NextTrackOrderAsync(target.Id!)));
            });

        public Task ReorderTracksAsync(string campaignId, string collectionId, List<string> ids) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var c = await CollectionAsync(campaignId, collectionId);
                var songs = await _db.Tracks.Find(t => t.CollectionId == c.Id).ToListAsync();
                SameSet(ids, songs.Select(t => t.Id!), "The collection changed. Try again.");
                for (var i = 0; i < ids.Count; i++)
                    await _db.Tracks.UpdateOneAsync(t => t.Id == ids[i], Builders<MusicTrack>.Update.Set(t => t.Order, i + 1));
            });

        public Task DeleteTrackAsync(string campaignId, string id) =>
            LibraryOpAsync(campaignId, async (_, p) =>
            {
                var t = await OwnTrackAsync(campaignId, id);
                await DropFromQueueAsync(p, new HashSet<string> { id });
                await _db.Tracks.DeleteOneAsync(x => x.Id == id);
                await ReleaseAsync(campaignId, new[] { t.FileId, t.CoverFileId });
            });

        private async Task<List<string>> SongsInAsync(string campaignId, string collectionId)
        {
            var c = await CollectionAsync(campaignId, collectionId);
            return (await _db.Tracks.Find(t => t.CollectionId == c.Id).SortBy(t => t.Order).ToListAsync()).Select(t => t.Id!).ToList();
        }

        public Task<object> PlayCollectionAsync(string campaignId, string collectionId, string? from) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                var order = await SongsInAsync(campaignId, collectionId);
                if (order.Count == 0) throw new ArgumentException("That collection has no songs yet.");
                var start = from != null ? Math.Max(0, order.IndexOf(from)) : 0;
                var queue = p.Shuffle ? new[] { order[start] }.Concat(Shuffled(order.Where((_, i) => i != start))).ToList() : order;
                var change = FromTop(p.Shuffle ? 0 : start);
                change.Queue = queue;
                change.Order = order;
                change.Playing = true;
                await CommitAsync(p, change);
            });

        public Task<object> EnqueueAsync(string campaignId, List<string>? songs, string? collectionId) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                var ids = collectionId != null ? await SongsInAsync(campaignId, collectionId) : songs ?? new();
                foreach (var id in ids) await OwnTrackByIdAsync(campaignId, id);
                if (ids.Count == 0) return;
                var empty = p.Queue.Count == 0;
                var change = empty ? FromTop(0) : new Change();
                change.Queue = p.Queue.Concat(ids).ToList();
                change.Order = p.Order.Concat(ids).ToList();
                if (empty) change.Playing = true;
                await CommitAsync(p, change);
            });

        public Task<object> PlayNowAsync(string campaignId, string song) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                await OwnTrackByIdAsync(campaignId, song);
                var at = p.Queue.Count > 0 ? p.Index + 1 : 0;
                var queue = p.Queue.ToList();
                queue.Insert(at, song);
                var cur = p.Queue.Count > 0 ? p.Order.IndexOf(p.Queue[p.Index]) : -1;
                var order = p.Order.ToList();
                order.Insert(cur + 1, song);
                var change = FromTop(at);
                change.Queue = queue;
                change.Order = order;
                change.Playing = true;
                await CommitAsync(p, change);
            });

        private static void SameEntry(MusicPlayer p, int index, string song)
        {
            if (index < 0 || index >= p.Queue.Count || p.Queue[index] != song) throw new ConcurrencyException("The queue changed. Try again.");
        }

        public Task<object> JumpAsync(string campaignId, int index, string song) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                SameEntry(p, index, song);
                await CommitAsync(p, FromTop(index));
            });

        public Task<object> MoveQueuedAsync(string campaignId, int from, int to, string song) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                SameEntry(p, from, song);
                if (to < 0 || to >= p.Queue.Count) throw new ConcurrencyException("The queue changed. Try again.");
                var (queue, index) = MoveInQueue(p.Queue, p.Index, from, to);
                await CommitAsync(p, new Change { Queue = queue, Index = index, Order = p.Shuffle ? null : queue });
            });

        public Task<object> RemoveFromQueueAsync(string campaignId, int index, string song) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                SameEntry(p, index, song);
                var queue = p.Queue.Where((_, i) => i != index).ToList();
                var o = p.Order.IndexOf(song);
                var order = p.Order.Where((_, i) => i != o).ToList();
                var change = index == p.Index ? FromTop(index) : new Change { Index = index < p.Index ? p.Index - 1 : p.Index };
                change.Queue = queue;
                change.Order = order;
                await CommitAsync(p, change);
            });

        public Task<object> ClearQueueAsync(string campaignId) =>
            PlayerOpAsync(campaignId, true, (_, p) =>
                CommitAsync(p, new Change { Queue = new(), Order = new(), Index = 0, Playing = false, Position = 0 }));

        public Task<object> PlayAsync(string campaignId) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                if (p.Playing || p.Queue.Count == 0) return;
                var track = await CurrentAsync(p);
                var from = track != null && p.Position >= track.Duration - 0.5 ? 0 : p.Position;
                await CommitAsync(p, new Change { Playing = true, StartedAt = Now - (long)(from * 1000) });
            });

        public Task<object> PauseAsync(string campaignId) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                if (!p.Playing) return;
                await CommitAsync(p, new Change { Playing = false, Position = (Now - p.StartedAt) / 1000.0 });
            });

        public Task<object> SeekAsync(string campaignId, double seconds) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                var track = await CurrentAsync(p);
                if (track == null || double.IsNaN(seconds)) return;
                var at = Math.Clamp(seconds, 0, Math.Max(0, track.Duration - 0.25));
                await CommitAsync(p, p.Playing ? new Change { StartedAt = Now - (long)(at * 1000) } : new Change { Position = at });
            });

        public Task<object> SkipAsync(string campaignId, string kind) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                var change = Step(p, kind == "prev" ? -1 : 1);
                if (change != null) await CommitAsync(p, change);
            });

        public Task<object> SetShuffleAsync(string campaignId, bool on) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                if (p.Shuffle == on) return;
                if (on)
                {
                    var queue = p.Queue.Take(p.Index + 1).Concat(Shuffled(p.Queue.Skip(p.Index + 1))).ToList();
                    await CommitAsync(p, new Change { Shuffle = true, Queue = queue });
                }
                else
                {
                    var current = p.Queue.ElementAtOrDefault(p.Index);
                    await CommitAsync(p, new Change { Shuffle = false, Queue = p.Order, Index = Math.Max(0, current == null ? 0 : p.Order.IndexOf(current)) });
                }
            });

        public Task<object> SetRepeatAsync(string campaignId, string mode) =>
            PlayerOpAsync(campaignId, true, async (_, p) =>
            {
                if (!RepeatModes.Contains(mode)) throw new ArgumentException("Repeat is off, all or one.");
                if (p.Repeat != mode) await CommitAsync(p, new Change { Repeat = mode });
            });

        public Task<object> SetSettingsAsync(string campaignId, double? crossfade, string? easing, bool? softPause) =>
            PlayerOpAsync(campaignId, true, async (campaign, p) =>
            {
                if (easing != null && !Easings.Contains(easing)) throw new ArgumentException("Unknown fade curve.");
                if (crossfade != null && !(crossfade >= 0 && crossfade <= MaxCrossfade))
                    throw new ArgumentException($"A crossfade runs from 0 to {MaxCrossfade} seconds.");
                var before = p.Crossfade;
                if (crossfade != null) p.Crossfade = Math.Round(crossfade.Value * 2) / 2;
                if (easing != null) p.Easing = easing;
                if (softPause != null) p.SoftPause = softPause.Value;
                if (p.Crossfade != before && p.Playing) await CommitAsync(p, new Change());
                await PushLibraryAsync(campaign);
            });

        public Task<object> RequestSkipAsync(string campaignId, string kind) =>
            PlayerOpAsync(campaignId, false, async (campaign, p) =>
            {
                if (Drives(campaign)) throw new ArgumentException("You can skip directly.");
                if (p.Vote?.Status == "open") throw new AlreadyExistsException("There is already a request on the table.");
                if (p.Queue.Count == 0) throw new ArgumentException("Nothing is playing.");
                if (Step(p, kind == "prev" ? -1 : 1) == null) throw new ArgumentException("That was the last song in the queue.");
                var now = Now;
                p.Vote = new MusicVote
                {
                    Kind = kind == "prev" ? "prev" : "next",
                    By = Me,
                    ByName = _access.User?.Identity?.Name ?? "Someone",
                    Track = p.Queue[p.Index],
                    Index = p.Index,
                    Eligible = _runtime.PresentOthers(campaignId, Me) + 1,
                    Yes = new() { Me },
                    StartedAt = now,
                    EndsAt = now + VoteMs,
                };
                await TallyAsync(p);
            });

        public Task<object> VoteAsync(string campaignId, string voteId, bool yes) =>
            PlayerOpAsync(campaignId, false, async (campaign, p) =>
            {
                var vote = p.Vote;
                if (vote == null || vote.Id != voteId || vote.Status != "open") throw new ConcurrencyException("That request has already been settled.");
                if (Drives(campaign))
                {
                    await CloseVoteAsync(p, yes, "dm");
                    return;
                }
                vote.Yes.Remove(Me);
                vote.No.Remove(Me);
                (yes ? vote.Yes : vote.No).Add(Me);
                vote.Eligible = Math.Max(vote.Eligible, vote.Yes.Count + vote.No.Count);
                await TallyAsync(p);
            });

        private async Task<AmbienceLayer> OwnLayerAsync(string campaignId, string id) =>
            await _db.Layers.Find(l => l.Id == id && l.CampaignId == campaignId).FirstOrDefaultAsync()
                ?? throw new NotFoundException("That layer no longer exists.");

        public Task AddLayerAsync(string campaignId, string collectionId, Stream audio, string? fileName, string? name) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var collection = await CollectionAsync(campaignId, collectionId);
                if (collection.Kind != MusicKinds.Situation) throw new ArgumentException("Ambience belongs to a collection of your own.");
                var file = await _files.UploadAsync(audio, StorageFolder.Music, Me, fileName);
                try
                {
                    var last = await _db.Layers.Find(l => l.CollectionId == collection.Id).SortByDescending(l => l.Order).FirstOrDefaultAsync();
                    var clean = string.IsNullOrWhiteSpace(name) ? "Ambience" : CleanName(name, "Give the layer a name.");
                    await _db.Layers.InsertOneAsync(new AmbienceLayer
                    {
                        CampaignId = campaignId,
                        CollectionId = collection.Id!,
                        FileId = file.Id!,
                        Name = clean,
                        Order = (last?.Order ?? 0) + 1,
                    });
                }
                catch
                {
                    await _files.DeleteAsync(file.Id!);
                    throw;
                }
            });

        public Task RenameLayerAsync(string campaignId, string id, string name) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var clean = CleanName(name, "Give the layer a name.");
                await OwnLayerAsync(campaignId, id);
                await _db.Layers.UpdateOneAsync(l => l.Id == id, Builders<AmbienceLayer>.Update.Set(l => l.Name, clean));
            });

        public Task SetLayerAsync(string campaignId, string id, double? level, bool? on) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                if (level != null && !(level >= 0 && level <= 1)) throw new ArgumentException("A level runs from 0 to 1.");
                await OwnLayerAsync(campaignId, id);
                var update = new List<UpdateDefinition<AmbienceLayer>>();
                if (level != null) update.Add(Builders<AmbienceLayer>.Update.Set(l => l.Level, level.Value));
                if (on != null) update.Add(Builders<AmbienceLayer>.Update.Set(l => l.On, on.Value));
                if (update.Count > 0) await _db.Layers.UpdateOneAsync(l => l.Id == id, Builders<AmbienceLayer>.Update.Combine(update));
            });

        public Task ReorderLayersAsync(string campaignId, string collectionId, List<string> ids) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var c = await CollectionAsync(campaignId, collectionId);
                var layers = await _db.Layers.Find(l => l.CollectionId == c.Id).ToListAsync();
                SameSet(ids, layers.Select(l => l.Id!), "The layers changed. Try again.");
                for (var i = 0; i < ids.Count; i++)
                    await _db.Layers.UpdateOneAsync(l => l.Id == ids[i], Builders<AmbienceLayer>.Update.Set(l => l.Order, i + 1));
            });

        public Task MoveLayerToAsync(string campaignId, string id, string collectionId) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var layer = await OwnLayerAsync(campaignId, id);
                if (layer.CollectionId == collectionId) return;
                var target = await CollectionAsync(campaignId, collectionId);
                if (target.Kind != MusicKinds.Situation) throw new ArgumentException("Ambience belongs to a collection of your own.");
                var last = await _db.Layers.Find(l => l.CollectionId == target.Id).SortByDescending(l => l.Order).FirstOrDefaultAsync();
                await _db.Layers.UpdateOneAsync(l => l.Id == id, Builders<AmbienceLayer>.Update
                    .Set(l => l.CollectionId, target.Id!)
                    .Set(l => l.Order, (last?.Order ?? 0) + 1));
            });

        public Task DeleteLayerAsync(string campaignId, string id) =>
            LibraryOpAsync(campaignId, async (_, _) =>
            {
                var layer = await OwnLayerAsync(campaignId, id);
                await _db.Layers.DeleteOneAsync(l => l.Id == id);
                await ReleaseAsync(campaignId, new[] { layer.FileId });
            });

        public Task PlayAmbienceAsync(string campaignId, string? collectionId) =>
            LibraryOpAsync(campaignId, async (_, p) =>
            {
                if (collectionId != null)
                {
                    var c = await CollectionAsync(campaignId, collectionId);
                    var layers = await _db.Layers.Find(l => l.CollectionId == c.Id).ToListAsync();
                    if (layers.Count == 0) throw new ArgumentException("Add a layer first.");
                    if (!layers.Any(l => l.On)) throw new ArgumentException("Switch on a layer first.");
                }
                p.AmbienceCollectionId = collectionId;
            });
    }
}
