using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class MonsterService : BaseService<Monster, IMonsterRepository>, IMonsterService
    {
        public MonsterService(IMonsterRepository repository, ILogger logger) : base(repository, logger)
        {
        }

        public Task<List<Monster>> GetMonstersByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Monster name cannot be null or empty.");

            return _repository.FindByNamePhraseAsync(name);
        }

        public Task<List<Monster>> GetPagedMonstersAsync(int page, int pageSize)
        {
            if (page <= 0 || pageSize <= 0)
                throw new ArgumentException("Page and page size must be greater than zero.");
            return _repository.GetPagedAsync(page, pageSize);
        }

        public Task<List<Monster>> SearchMonstersAsync(string query, int page, int pageSize)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be null or empty.");
            if (page <= 0 || pageSize <= 0)
                throw new ArgumentException("Page and page size must be greater than zero.");
            return _repository.SearchAsync(query, page, pageSize);
        }

        public async Task<bool> DeleteOwnMonsterAsync(string monsterId, string userId)
        {
            if (string.IsNullOrWhiteSpace(monsterId))
                throw new ArgumentException("Monster ID cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be null or empty.");

            var monster = await _repository.GetByIdAsync(monsterId);
            if (monster == null) return false;

            // Only allow delete if user owns the monster
            if (monster.CreatedByUserId != userId)
                throw new UnauthorizedAccessException("User does not own this monster.");

            await _repository.DeleteAsync(monsterId);
            return true;
        }

        public async Task<List<Monster>> GetMonstersByOwnerAsync(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
                throw new ArgumentException("Owner ID cannot be null or empty.");

            // Assuming repository supports filtering by owner
            var monsters = await _repository.FindByOwnerIdAsync(ownerId);
            return monsters;
        }

        public async Task<bool> SwitchMonsterOwnerAsync(string monsterId, string newOwnerId, string requesterUserId)
        {
            if (string.IsNullOrWhiteSpace(monsterId))
                throw new ArgumentException("Monster ID cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(newOwnerId))
                throw new ArgumentException("New owner ID cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(requesterUserId))
                throw new ArgumentException("Requester user ID cannot be null or empty.");

            var monster = await _repository.GetByIdAsync(monsterId);
            if (monster == null) return false;

            // Check if requester is allowed (e.g., is current owner or admin)
            if (monster.CreatedByUserId != requesterUserId)
                throw new UnauthorizedAccessException("User is not allowed to switch ownership.");

            monster.CreatedByUserId = newOwnerId;
            await _repository.UpdateAsync(monster);
            return true;
        }

        public async Task<bool> AddMonsterOwnerAsync(string monsterId, string newOwnerId, string requesterUserId)
        {
            if (string.IsNullOrWhiteSpace(monsterId))
                throw new ArgumentException("Monster ID cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(newOwnerId))
                throw new ArgumentException("New owner ID cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(requesterUserId))
                throw new ArgumentException("Requester user ID cannot be null or empty.");

            var monster = await _repository.GetByIdAsync(monsterId);
            if (monster == null) return false;

            // Check if requester is allowed (e.g., is current owner or admin)
            if (monster.CreatedByUserId != requesterUserId)
                throw new UnauthorizedAccessException("User is not allowed to add owners.");

            // Add new owner
            if (!monster.OwnerIds!.Contains(newOwnerId))
            {
                monster.OwnerIds.Add(newOwnerId);
                await _repository.UpdateAsync(monster);
            }
            return true;
        }

        public async Task<List<Monster>> AdvancedSearchAsync(MonsterSearchCriteria criteria)
        {
            if (criteria == null)
                throw new ArgumentNullException(nameof(criteria));

            // Call repository to do the filtering
            var monsters = await _repository.SearchAsync(criteria);

            return monsters;
        }

    }
}
