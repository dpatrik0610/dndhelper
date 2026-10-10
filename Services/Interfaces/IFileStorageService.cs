using dndhelper.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    /// <summary>
    /// Server-side only access to the R2 bucket; no controller exposes it directly. Feature services
    /// (token image, item image...) authorise the caller first, then call this.
    /// </summary>
    public interface IFileStorageService
    {
        /// <summary>Validates (size + real type for the folder), stores in R2, records it in Mongo. Throws ArgumentException on a rejected file.</summary>
        Task<StoredFile> UploadAsync(Stream content, StorageFolder folder, string ownerId, string? fileName = null, CancellationToken ct = default);

        /// <summary>
        /// Uploads the new file, runs attach (save its Url/Id on the entity), then deletes the old file.
        /// If attach fails the new file is removed and the old one kept, so the entity never points at a missing file.
        /// oldFileId must come from the entity the caller already authorised; null = nothing to remove.
        /// </summary>
        Task<StoredFile> ReplaceAsync(string? oldFileId, Stream content, StorageFolder folder, string ownerId,
            Func<StoredFile, Task> attach, string? fileName = null, CancellationToken ct = default);

        Task<StoredFile?> GetAsync(string fileId);

        /// <summary>Short-lived read URL, for a private bucket.</summary>
        Task<string> GetSignedUrlAsync(StoredFile file, TimeSpan lifetime);

        /// <summary>Removes the object and soft-deletes the record. False when it doesn't exist.</summary>
        Task<bool> DeleteAsync(string fileId, CancellationToken ct = default);
    }
}
