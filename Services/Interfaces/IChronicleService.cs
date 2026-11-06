using dndhelper.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IChronicleService
    {
        Task<List<ChronicleEntry>> GetEntriesAsync(string campaignId);
        Task<ChronicleEntry> GetEntryAsync(string id);
        Task<ChronicleEntry> CreateEntryAsync(ChronicleEntryRequest request);
        Task<ChronicleEntry> UpdateEntryAsync(string id, ChronicleEntryRequest request);
        Task<bool> DeleteEntryAsync(string id);
        Task<string> UploadImageAsync(string campaignId, Stream content, string? fileName);

        Task<List<ChronicleFolder>> GetFoldersAsync(string campaignId);
        Task<ChronicleFolder> CreateFolderAsync(ChronicleFolderRequest request);
        Task<ChronicleFolder> UpdateFolderAsync(string id, ChronicleFolderRequest request);
        Task<bool> DeleteFolderAsync(string id);

        Task<List<ChronicleComment>> GetCommentsAsync(string entryId);
        Task<ChronicleComment> AddCommentAsync(string entryId, ChronicleCommentRequest request);
        Task<bool> DeleteCommentAsync(string entryId, string commentId);
    }
}
