using dndhelper.Authorization;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class BaseService<T, TRepository> : IBaseService<T>, IInternalBaseService<T>
        where T : class, IEntity
        where TRepository : IRepository<T>
    {
        protected readonly TRepository _repository;
        protected readonly ILogger _logger;
        protected readonly IAuthorizationService _authorizationService;
        protected readonly ClaimsPrincipal _user;

        public BaseService(
            TRepository repository,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _user = httpContextAccessor?.HttpContext?.User ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        public virtual async Task<T?> CreateAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var userId = GetCurrentUserId();
            entity.CreatedAt = DateTime.UtcNow;
            AttachOwnerIfNeeded(entity, userId);

            _logger.Debug("Creating entity of type {EntityType}", typeof(T).Name);
            return await _repository.CreateAsync(entity);
        }

        public virtual async Task<List<T>> CreateManyAsync(List<T> entities)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            var userId = GetCurrentUserId();
            var now = DateTime.UtcNow;

            foreach (var entity in entities)
            {
                entity.CreatedAt = now;
                AttachOwnerIfNeeded(entity, userId);
            }

            _logger.Debug("Creating {Count} entities of type {EntityType}", entities.Count, typeof(T).Name);
            return await _repository.CreateManyAsync(entities);
        }

        public virtual async Task<T?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var entity = await _repository.GetByIdAsync(id);
            if (entity is IOwnedResource owned)
                await EnsureOwnershipAccess(owned);

            return entity;
        }

        public virtual async Task<List<T>> GetByIdsAsync(IEnumerable<string> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));

            var entities = await _repository.GetByIdsAsync(ids);
            return await FilterOwnedResourcesAsync(entities);
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            var entities = await _repository.GetAllAsync();
            return await FilterOwnedResourcesAsync(entities);
        }

        public virtual async Task<long> CountAsync()
        {
            var all = await _repository.GetAllAsync();

            if (typeof(IOwnedResource).IsAssignableFrom(typeof(T)))
            {
                var filtered = await FilterOwnedResourcesAsync(all);
                return filtered.LongCount();
            }

            return await _repository.CountAsync();
        }

        public virtual async Task<bool> ExistsAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return false;

            if (entity is IOwnedResource owned)
            {
                var authorized = await _authorizationService.AuthorizeAsync(_user, owned, "OwnershipPolicy");
                return authorized.Succeeded;
            }

            return true;
        }

        public virtual async Task<T?> UpdateAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            if (entity is IOwnedResource owned)
                await EnsureOwnershipAccess(owned);

            entity.UpdatedAt = DateTime.UtcNow;
            return await _repository.UpdateAsync(entity);
        }

        public virtual async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var entity = await _repository.GetByIdAsync(id);
            if (entity is IOwnedResource owned)
                await EnsureOwnershipAccess(owned);

            return await _repository.DeleteAsync(id);
        }

        public virtual async Task<bool> LogicDeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var entity = await _repository.GetByIdAsync(id);
            if (entity is IOwnedResource owned)
                await EnsureOwnershipAccess(owned);

            return await _repository.LogicDeleteAsync(id);
        }

        protected async Task EnsureOwnershipAccess(IOwnedResource owned)
        {
            var result = await _authorizationService.AuthorizeAsync(_user, owned, "OwnershipPolicy");
            if (!result.Succeeded)
            {
                _logger.Warning("Unauthorized access attempt by {User} on {Resource}",
                    _user.Identity?.Name ?? "Unknown", typeof(T).Name);

                throw new UnauthorizedAccessException("You do not have permission to access this resource.");
            }
        }

        protected async Task<List<T>> FilterOwnedResourcesAsync(IEnumerable<T> entities)
        {
            var result = new List<T>();

            foreach (var entity in entities)
            {
                if (entity is IOwnedResource owned)
                {
                    var authResult = await _authorizationService.AuthorizeAsync(_user, owned, "OwnershipPolicy");
                    if (authResult.Succeeded)
                        result.Add(entity);
                }
                else
                {
                    result.Add(entity);
                }
            }

            return result;
        }

        #region Internal - No ownership checks

        public virtual async Task<T?> GetByIdInternalAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            return await ExecuteInternalAsync(() => _repository.GetByIdAsync(id), "GetById", id);
        }

        public virtual async Task<List<T>> GetByIdsInternalAsync(IEnumerable<string> ids)
        {
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));

            return await ExecuteInternalAsync(() => _repository.GetByIdsAsync(ids), "GetByIds");
        }

        public virtual async Task<IEnumerable<T>> GetAllInternalAsync()
        {
            return await ExecuteInternalAsync(() => _repository.GetAllAsync(), "GetAll");
        }

        public virtual async Task<T?> UpdateInternalAsync(T entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            entity.UpdatedAt = DateTime.UtcNow;
            return await ExecuteInternalAsync(() => _repository.UpdateAsync(entity), "Update");
        }

        public virtual async Task<long> CountInternalAsync()
        {
            return await ExecuteInternalAsync(() => _repository.CountAsync(), "Count");
        }

        public virtual async Task<bool> ExistsInternalAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var entity = await GetByIdInternalAsync(id);
            return entity != null;
        }

        #endregion

        #region Helpers

        private string? GetCurrentUserId()
        {
            if (_user.Identity?.IsAuthenticated != true)
                return null;

            return _user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private static void AttachOwnerIfNeeded(T entity, string? userId)
        {
            if (string.IsNullOrEmpty(userId))
                return;

            if (entity is not IOwnedResource owned)
                return;

            owned.OwnerIds ??= new List<string>();
            if (!owned.OwnerIds.Contains(userId))
                owned.OwnerIds.Add(userId);
        }

        private async Task<TResult> ExecuteInternalAsync<TResult>(
            Func<Task<TResult>> action,
            string operation,
            string? id = null)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                if (id == null)
                {
                    _logger.Error(ex,
                        "Internal {Operation} failed for {EntityType}",
                        operation, typeof(T).Name);
                }
                else
                {
                    _logger.Error(ex,
                        "Internal {Operation} failed for {EntityType} with id {Id}",
                        operation, typeof(T).Name, id);
                }

                throw;
            }
        }

        #endregion
    }
}
