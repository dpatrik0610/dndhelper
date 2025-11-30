using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.SignalR
{
    /// <summary>
    /// Generic service for broadcasting entity changes via SignalR
    /// </summary>
    public interface IEntitySyncService
    {
        Task BroadcastEntityCreated<T>(string entityType, T entity, string createdBy, string? targetUserId = null);
        Task BroadcastEntityUpdated<T>(string entityType, string entityId, T entity, string updatedBy, string? targetUserId = null);
        Task BroadcastEntityDeleted(string entityType, string entityId, string deletedBy, string? targetUserId = null);
        Task BroadcastToUsers<T>(string eventName, T data, List<string> userIds, string? excludeUserId = null);

        // Batch
        Task BroadcastEntityBatchToUsers(IEnumerable<object> changes, IEnumerable<string> userIds, string? correlationId = null, string? excludeUserId = null);
        Task BroadcastEntityBatchToAll(IEnumerable<object> changes, string? correlationId = null);

    }
}
