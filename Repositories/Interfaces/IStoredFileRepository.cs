using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IStoredFileRepository : IRepository<StoredFile>
    {
        /// <summary>Live (not deleted) files the user owns in one folder.</summary>
        Task<long> CountAsync(string ownerId, StorageFolder folder);

        /// <summary>Live files the user owns in one folder, newest first.</summary>
        Task<List<StoredFile>> ListAsync(string ownerId, StorageFolder folder);

        /// <summary>The live file behind a public URL, if it is one of ours.</summary>
        Task<StoredFile?> GetByUrlAsync(string url);
    }
}
