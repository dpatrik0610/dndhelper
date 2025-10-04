using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace dndhelper.Services
{
    public class BaseService<T, TRepository> : IBaseService<T>
        where T : class, IEntity
        where TRepository : IRepository<T>
    {
        protected readonly TRepository _repository;
        protected readonly ILogger _logger;

        public BaseService(TRepository repository, ILogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ------------------------
        // CREATE
        // ------------------------
        public virtual async Task<T?> CreateAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _logger.Debug("Creating entity of type {EntityType}", typeof(T).Name);
            return await _repository.CreateAsync(entity);
        }

        public virtual async Task<List<T>> CreateManyAsync(List<T> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            _logger.Debug("Creating {Count} entities of type {EntityType}", entities.Count, typeof(T).Name);
            return await _repository.CreateManyAsync(entities);
        }

        // ------------------------
        // READ
        // ------------------------
        public virtual async Task<T?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await _repository.GetByIdAsync(id);
        }

        public virtual async Task<List<T>> GetByIdsAsync(IEnumerable<string> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            return await _repository.GetByIdsAsync(ids);
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _repository.GetAllAsync();
        }

        public virtual async Task<long> CountAsync()
        {
            return await _repository.CountAsync();
        }

        public virtual async Task<bool> ExistsAsync(string id)
        {
            return await _repository.ExistsAsync(id);
        }

        // ------------------------
        // UPDATE
        // ------------------------
        public virtual async Task<T?> UpdateAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            return await _repository.UpdateAsync(entity);
        }

        // ------------------------
        // DELETE
        // ------------------------
        public virtual async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await _repository.DeleteAsync(id);
        }

        public virtual async Task<bool> LogicDeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await _repository.LogicDeleteAsync(id);
        }
    }
}
