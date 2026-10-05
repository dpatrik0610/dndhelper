using dndhelper.Authorization;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using MongoDB.Driver;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly HttpContext _httpContext;
        private CampaignAccess? _access;

        // Resolved per request; derived services don't need another constructor parameter.
        protected CampaignAccess Access => _access ??= _httpContext.RequestServices.GetRequiredService<CampaignAccess>();

        public BaseService(
            TRepository repository,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _httpContext = httpContextAccessor?.HttpContext ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _user = _httpContext.User;
        }

        public virtual async Task<T?> CreateAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var userId = GetCurrentUserId();
            entity.CreatedAt = DateTime.UtcNow;
            AttachOwnerIfNeeded(entity, userId);
            if (entity is ICampaignScoped scoped)
                await Access.PrepareCreateAsync(scoped);

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
                if (entity is ICampaignScoped scoped)
                    await Access.PrepareCreateAsync(scoped);
            }

            _logger.Debug("Creating {Count} entities of type {EntityType}", entities.Count, typeof(T).Name);
            return await _repository.CreateManyAsync(entities);
        }

        public virtual async Task<T?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
                await EnsureReadAccess(entity);

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
            return await FilterOwnedResourcesAsync(await GetAllInScopeAsync());
        }

        public virtual async Task<long> CountAsync()
        {
            if (typeof(IOwnedResource).IsAssignableFrom(typeof(T)) || typeof(ICampaignScoped).IsAssignableFrom(typeof(T)))
                return (await GetAllAsync()).LongCount();

            return await _repository.CountAsync();
        }

        public virtual async Task<bool> ExistsAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return false;

            return await CanReadAsync(entity);
        }

        public virtual async Task<T?> UpdateAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            // Authorize against the stored document, never the client-supplied one.
            if (entity.Id != null && await _repository.GetByIdAsync(entity.Id) is T existing)
                await EnsureWriteAccess(existing);

            entity.UpdatedAt = DateTime.UtcNow;
            return await _repository.UpdateAsync(entity);
        }

        public virtual async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
                await EnsureWriteAccess(entity);

            return await _repository.DeleteAsync(id);
        }

        public virtual async Task<bool> LogicDeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
                await EnsureWriteAccess(entity);

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
                if (await CanReadAsync(entity))
                    result.Add(entity);
            }

            return result;
        }

        /// <summary>Repository query narrowed to the caller's campaign scope (all docs for non-scoped types).</summary>
        protected async Task<IEnumerable<T>> GetAllInScopeAsync()
        {
            if (!typeof(ICampaignScoped).IsAssignableFrom(typeof(T)))
                return await _repository.GetAllAsync();

            return await _repository.FindAsync(await ScopeFilterAsync());
        }

        /// <summary>CampaignAccess.ReadFilterAsync for T (T is only known to be ICampaignScoped at runtime).</summary>
        protected Task<FilterDefinition<T>> ScopeFilterAsync() =>
            (Task<FilterDefinition<T>>)typeof(CampaignAccess)
                .GetMethod(nameof(CampaignAccess.ReadFilterAsync))!
                .MakeGenericMethod(typeof(T))
                .Invoke(Access, null)!;

        // Campaign content (spells, items, monsters, rules) is governed by campaign roles only;
        // everything else additionally keeps its per-owner check.
        protected async Task<bool> CanReadAsync(T entity)
        {
            if (entity is ICampaignScoped scoped && !await Access.CanReadAsync(scoped))
                return false;
            if (entity is IOwnedResource owned && entity is not ICampaignContent)
                return (await _authorizationService.AuthorizeAsync(_user, owned, "OwnershipPolicy")).Succeeded;
            return true;
        }

        protected async Task EnsureReadAccess(T entity)
        {
            if (entity is ICampaignScoped scoped)
                await Access.EnsureCanReadAsync(scoped);
            if (entity is IOwnedResource owned && entity is not ICampaignContent)
                await EnsureOwnershipAccess(owned);
        }

        protected async Task EnsureWriteAccess(T entity)
        {
            if (entity is ICampaignScoped scoped)
                await Access.EnsureCanWriteAsync(scoped);
            if (entity is IOwnedResource owned && entity is not ICampaignContent)
                await EnsureOwnershipAccess(owned);
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

        protected string? GetCurrentUserId()
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
