using dndhelper.Core;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services.SignalR
{
    public record EntityChangePayload<T>(
        string EntityType,
        string Action,
        string? EntityId,
        T? Data,
        string ChangedBy,
        DateTime Timestamp
    );

    public record EntityChangeBatch(
        string CorrelationId,
        DateTime Timestamp,
        IReadOnlyList<object> Changes
    );

    public class EntitySyncService : IEntitySyncService
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger _logger;

        public EntitySyncService(IHubContext<NotificationHub> hubContext, ILogger logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        // Centralized internal helper
        private Task BroadcastEntityChangedInternalAsync<T>(
            string entityType,
            string action,
            string? entityId,
            T? entity,
            string changedBy,
            string? targetUserId = null)
        {
            var payload = new EntityChangePayload<T>(
                entityType,
                action,
                entityId,
                entity,
                changedBy,
                DateTime.UtcNow
            );

            if (!string.IsNullOrEmpty(targetUserId))
            {
                _logger.Information(
                    "📤 Broadcasting {EntityType} ({EntityId}) {Action} to user {UserId}",
                    entityType, entityId, action, targetUserId
                );

                return _hubContext.Clients
                    .Group($"user_{targetUserId}")
                    .SendAsync("EntityChanged", payload);
            }

            _logger.Information(
                "📤 Broadcasting {EntityType} ({EntityId}) {Action} to all users",
                entityType, entityId, action
            );

            return _hubContext.Clients
                .All
                .SendAsync("EntityChanged", payload);
        }

        /// <summary>
        /// Broadcast entity creation to specific user or all users
        /// </summary>
        public Task BroadcastEntityCreated<T>(
            string entityType,
            T entity,
            string createdBy,
            string? targetUserId = null)
        {
            // entityId is null on create
            return BroadcastEntityChangedInternalAsync(
                entityType,
                "created",
                null,
                entity,
                createdBy,
                targetUserId
            );
        }

        /// <summary>
        /// Broadcast entity update to specific user or all users
        /// </summary>
        public Task BroadcastEntityUpdated<T>(
            string entityType,
            string entityId,
            T entity,
            string updatedBy,
            string? targetUserId = null)
        {
            return BroadcastEntityChangedInternalAsync(
                entityType,
                "updated",
                entityId,
                entity,
                updatedBy,
                targetUserId
            );
        }

        /// <summary>
        /// Broadcast entity deletion to specific user or all users
        /// </summary>
        public Task BroadcastEntityDeleted(
            string entityType,
            string entityId,
            string deletedBy,
            string? targetUserId = null)
        {
            return BroadcastEntityChangedInternalAsync<object?>(
                entityType,
                "deleted",
                entityId,
                null,
                deletedBy,
                targetUserId
            );
        }

        /// <summary>
        /// Broadcast custom data to multiple specific users (existing List&lt;string&gt; API)
        /// </summary>
        public Task BroadcastToUsers<T>(
            string eventName,
            T data,
            List<string> userIds,
            string? excludeUserId = null)
        {
            return BroadcastToUsers(eventName, data, (IEnumerable<string>)userIds);
        }

        /// <summary>
        /// Broadcast custom data to multiple specific users (IEnumerable&lt;string&gt; overload)
        /// </summary>
        public Task BroadcastToUsers<T>(
            string eventName,
            T data,
            IEnumerable<string> userIds,
            string? excludeUserId = null)
        {
            var distinct = userIds
                .Where(u => u != excludeUserId)
                .Distinct()
                .ToList();

            if (distinct.Count == 0)
                return Task.CompletedTask;

            _logger.Information(
                "📤 Broadcasting {EventName} to {Count} users (excluded: {Excluded})",
                eventName,
                distinct.Count,
                excludeUserId
            );

            var tasks = distinct.Select(userId =>
                _hubContext.Clients
                    .Group($"user_{userId}")
                    .SendAsync(eventName, data)
            );

            return Task.WhenAll(tasks);
        }


        /// <summary>
        /// Broadcast a batch of entity changes to multiple specific users
        /// </summary>
        public Task BroadcastEntityBatchToUsers(
            IEnumerable<object> changes,
            IEnumerable<string> userIds,
            string? correlationId = null,
            string? excludeUserId = null)
        {
            var changeList = changes.ToList();
            if (changeList.Count == 0)
                return Task.CompletedTask;

            var distinctUsers = userIds
                .Where(id => id != excludeUserId)
                .Distinct()
                .ToList();

            if (distinctUsers.Count == 0)
                return Task.CompletedTask;

            var batch = new EntityChangeBatch(
                correlationId ?? Guid.NewGuid().ToString("N"),
                DateTime.UtcNow,
                changeList
            );

            _logger.Information(
                "📤 Broadcasting batch {CorrelationId} with {ChangeCount} changes to {UserCount} users (excluded: {Excluded})",
                batch.CorrelationId,
                changeList.Count,
                distinctUsers.Count,
                excludeUserId
            );

            var tasks = distinctUsers.Select(userId =>
                _hubContext.Clients
                    .Group($"user_{userId}")
                    .SendAsync("EntityBatchChanged", batch)
            );

            return Task.WhenAll(tasks);
        }


        /// <summary>
        /// Broadcast a batch of entity changes to all users
        /// </summary>
        public Task BroadcastEntityBatchToAll(
            IEnumerable<object> changes,
            string? correlationId = null)
        {
            var changeList = changes.ToList();
            if (changeList.Count == 0)
                return Task.CompletedTask;

            var batch = new EntityChangeBatch(
                correlationId ?? Guid.NewGuid().ToString("N"),
                DateTime.UtcNow,
                changeList
            );

            _logger.Information(
                "📤 Broadcasting batch {CorrelationId} with {ChangeCount} changes to all users",
                batch.CorrelationId,
                changeList.Count
            );

            return _hubContext.Clients
                .All
                .SendAsync("EntityBatchChanged", batch);
        }
    }
}
