using Amazon.S3;
using Amazon.S3.Model;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    /// <summary>
    /// Stores files in Cloudflare R2 (S3 API) under images/ (maps in images/maps/, tokens in images/tokens/), music/ and docs/, with a StoredFile record in Mongo.
    /// Security: allowlisted types sniffed from the bytes, per-folder size caps enforced while reading,
    /// random keys (no user input in paths), docs served as attachments, credentials only from configuration.
    /// </summary>
    public class FileStorageService : IFileStorageService
    {
        private delegate (string ContentType, string Extension)? Sniffer(ReadOnlySpan<byte> head);

        private static readonly Dictionary<StorageFolder, (string Prefix, long MaxBytes, Sniffer Sniff)> Rules = new()
        {
            [StorageFolder.Images] = ("images", 15 * 1024 * 1024, FileSniffer.Image),
            [StorageFolder.Maps] = ("images/maps", 15 * 1024 * 1024, FileSniffer.Image),
            [StorageFolder.Tokens] = ("images/tokens", 5 * 1024 * 1024, FileSniffer.Image),
            [StorageFolder.Music] = ("music", 50 * 1024 * 1024, FileSniffer.Audio),
            [StorageFolder.Docs] = ("docs", 25 * 1024 * 1024, FileSniffer.Document),
            [StorageFolder.Monsters] = ("images/monsters", 10 * 1024 * 1024, FileSniffer.Image),
        };

        private static readonly TimeSpan MaxSignedUrlLifetime = TimeSpan.FromDays(7); // SigV4 limit

        // Lazy: the client is only built (and R2 settings checked) on first use, so services that merely
        // depend on storage still work when R2 isn't configured.
        private readonly Lazy<IAmazonS3> _s3;
        private readonly IStoredFileRepository _files;
        private readonly R2Options _options;
        private readonly ILogger _logger;

        public FileStorageService(Lazy<IAmazonS3> s3, IStoredFileRepository files, IOptions<R2Options> options, ILogger logger)
        {
            _s3 = s3;
            _files = files;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<StoredFile> UploadAsync(Stream content, StorageFolder folder, string ownerId, string? fileName = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("An owner is required.", nameof(ownerId));
            if (!Rules.TryGetValue(folder, out var rule)) throw new ArgumentException("Unknown folder.", nameof(folder));

            await using var buffer = await ReadCappedAsync(content, rule.MaxBytes, ct);
            if (buffer.Length == 0) throw new ArgumentException("The file is empty.");

            var head = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, FileSniffer.HeadLength));
            var detected = rule.Sniff(head) ?? throw new ArgumentException($"This file type isn't allowed in {rule.Prefix}.");

            var key = $"{rule.Prefix}/{Guid.NewGuid():N}{detected.Extension}";
            var name = SafeName(fileName);

            var put = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = buffer,
                AutoCloseStream = false,
                ContentType = detected.ContentType,
                DisablePayloadSigning = true, // R2 doesn't support streaming SigV4; the body still goes over TLS
            };
            put.Headers.CacheControl = "public, max-age=31536000, immutable"; // keys are never reused
            if (folder == StorageFolder.Docs)
                put.Headers.ContentDisposition = $"attachment; filename=\"{name ?? "document" + detected.Extension}\"";

            await _s3.Value.PutObjectAsync(put, ct);

            var file = new StoredFile
            {
                Folder = folder,
                Key = key,
                Url = string.IsNullOrWhiteSpace(_options.PublicBaseUrl) ? null : $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}",
                ContentType = detected.ContentType,
                Size = buffer.Length,
                OriginalName = name,
                OwnerId = ownerId,
            };

            try
            {
                return await _files.CreateAsync(file);
            }
            catch
            {
                // Without a record nobody can find or delete the object, so don't leave it behind.
                await TryDeleteObjectAsync(key);
                throw;
            }
        }

        public async Task<StoredFile> ReplaceAsync(string? oldFileId, Stream content, StorageFolder folder, string ownerId,
            Func<StoredFile, Task> attach, string? fileName = null, CancellationToken ct = default)
        {
            var file = await UploadAsync(content, folder, ownerId, fileName, ct);

            try
            {
                await attach(file);
            }
            catch
            {
                await TryDeleteAsync(file.Id!);
                throw;
            }

            // The entity already points at the new file; a leftover old object costs storage, not correctness.
            if (!string.IsNullOrEmpty(oldFileId) && oldFileId != file.Id)
                await TryDeleteAsync(oldFileId);

            return file;
        }

        public Task<StoredFile?> GetAsync(string fileId) => _files.GetByIdAsync(fileId);

        public Task<StoredFile?> GetByUrlAsync(string url) => _files.GetByUrlAsync(url);

        public Task<long> CountAsync(string ownerId, StorageFolder folder) => _files.CountAsync(ownerId, folder);

        public Task<List<StoredFile>> ListAsync(string ownerId, StorageFolder folder) => _files.ListAsync(ownerId, folder);

        public Task<string> GetSignedUrlAsync(StoredFile file, TimeSpan lifetime) =>
            _s3.Value.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = file.Key,
                Verb = HttpVerb.GET,
                Protocol = Protocol.HTTPS,
                Expires = DateTime.UtcNow.Add(lifetime > MaxSignedUrlLifetime ? MaxSignedUrlLifetime : lifetime),
            });

        public async Task<bool> DeleteAsync(string fileId, CancellationToken ct = default)
        {
            var file = await _files.GetByIdAsync(fileId);
            if (file == null) return false;

            await _s3.Value.DeleteObjectAsync(_options.BucketName, file.Key, ct);
            return await _files.LogicDeleteAsync(fileId);
        }

        /// <summary>Reads at most maxBytes, so an oversized or lying stream can't exhaust memory.</summary>
        private static async Task<MemoryStream> ReadCappedAsync(Stream content, long maxBytes, CancellationToken ct)
        {
            var tooBig = new ArgumentException($"The file can be at most {maxBytes / (1024 * 1024)} MB.");
            if (content.CanSeek && content.Length - content.Position > maxBytes) throw tooBig;

            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    await buffer.DisposeAsync();
                    throw tooBig;
                }
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            return buffer;
        }

        /// <summary>Display name only: no path, no control or quote characters, ASCII so it's safe in a header.</summary>
        internal static string? SafeName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            var name = new string(Path.GetFileName(fileName.Replace('\\', '/'))
                .Where(c => c >= 0x20 && c < 0x7F && c != '"')
                .Take(100)
                .ToArray()).Trim();
            return name.Length == 0 ? null : name;
        }

        private async Task TryDeleteAsync(string fileId)
        {
            try
            {
                await DeleteAsync(fileId);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not remove stored file {FileId}", fileId);
            }
        }

        private async Task TryDeleteObjectAsync(string key)
        {
            try
            {
                await _s3.Value.DeleteObjectAsync(_options.BucketName, key);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not remove orphaned R2 object {Key}", key);
            }
        }
    }
}
