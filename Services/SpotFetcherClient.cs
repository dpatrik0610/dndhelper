using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class SpotFetcherClient
    {
        private readonly HttpClient _http;

        public SpotFetcherClient(HttpClient http)
        {
            _http = http;
        }

        public async Task<HttpResponseMessage?> TrySendAsync(HttpMethod method, string path, object? body, bool stream, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = body == null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            try
            {
                return await _http.SendAsync(request, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        public async Task<MemoryStream> DownloadAsync(string path, long maxBytes, CancellationToken ct)
        {
            if (!path.StartsWith("/file/", StringComparison.Ordinal)) throw new ArgumentException("The helper sent something unexpected.");
            using var res = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            await using var from = await res.Content.ReadAsStreamAsync(ct);
            var into = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await from.ReadAsync(buffer, ct)) > 0)
            {
                if (into.Length + read > maxBytes) throw new ArgumentException($"That song is over {maxBytes / (1024 * 1024)} MB.");
                into.Write(buffer, 0, read);
            }
            into.Position = 0;
            return into;
        }
    }
}
