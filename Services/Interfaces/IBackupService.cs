using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public record BackupResult(Stream Stream, string FileName, string ContentType);

    public interface IBackupService
    {
        Task<BackupResult> ExportCollectionAsync(string collectionName, CancellationToken cancellationToken = default);
        Task RestoreCollectionAsync(string collectionName, Stream archiveStream, CancellationToken cancellationToken = default);
    }
}
