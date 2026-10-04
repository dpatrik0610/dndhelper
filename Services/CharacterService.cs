using dndhelper.Models.CharacterModels;
using dndhelper.Models.CharacterModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{

    public class CharacterService : BaseService<Character, ICharacterRepository> , ICharacterService
    {
        private readonly ITabletopService _tabletop;
        private readonly IUserRepository _users;

        public CharacterService(ICharacterRepository repository, ILogger logger, IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor, ITabletopService tabletop, IUserRepository users)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _tabletop = tabletop;
            _users = users;
        }

        /// <summary>A new character is listed on every owner's account (the creator is added as owner by the base).</summary>
        public override async Task<Character?> CreateAsync(Character entity)
        {
            var created = await base.CreateAsync(entity);
            if (created?.Id == null) return created;

            foreach (var ownerId in (created.OwnerIds ?? new List<string>()).Distinct())
                await _users.AddCharacterIdAsync(ownerId, created.Id);
            return created;
        }

        /// <summary>
        /// Characters on the user's list plus any they own but aren't listed for (characters created before
        /// creation linked them), so nothing goes missing.
        /// </summary>
        public async Task<List<Character>> GetForUserAsync(string userId, IEnumerable<string>? listedIds)
        {
            var listed = listedIds?.Any() == true ? await _repository.GetByIdsAsync(listedIds) : new List<Character>();
            var owned = await _repository.GetByOwnerIdAsync(userId);
            return listed
                .Concat(owned)
                .Where(c => c.Id != null && !c.IsDeleted)
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .ToList();
        }

        public Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId)
            => _repository.GetByOwnerIdAsync(ownerId);

        public override async Task<Character?> UpdateAsync(Character entity)
        {
            var updated = await base.UpdateAsync(entity);
            if (updated != null) await SyncTabletopAsync(updated);
            return updated;
        }

        /// <summary>Keeps tabletop tokens in step with the sheet; a table hiccup must not fail the sheet save.</summary>
        private async Task SyncTabletopAsync(Character character)
        {
            try
            {
                await _tabletop.SyncCharacterAsync(character);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Tabletop sync failed for character {CharacterId}", character.Id);
            }
        }
        public async Task<bool> UseSpellSlotAsync(string characterId, int level)
        {
            var character = await _repository.GetByIdAsync(characterId);
            if (character == null) return false;

            var slot = character.SpellSlots!.FirstOrDefault(s => s.Level == level);
            if (slot == null || slot.Current <= 0) return false;

            slot.Current--;
            await _repository.UpdateAsync(character);
            return true;
        }

        public async Task<bool> RecoverSpellSlotAsync(string characterId, int level, int amount = 1)
        {
            var character = await _repository.GetByIdAsync(characterId);
            if (character == null) return false;

            var slot = character.SpellSlots.FirstOrDefault(s => s.Level == level);
            if (slot == null) return false;

            slot.Current = Math.Min(slot.Current + amount, slot.Max);
            await _repository.UpdateAsync(character);
            return true;
        }

        public async Task<bool> LongRestAsync(string characterId)
        {
            var character = await _repository.GetByIdAsync(characterId);
            if (character == null) return false;

            // Recover all hit points and temporary hit points
            character.HitPoints = character.MaxHitPoints;
            character.TemporaryHitPoints = 0;

            // Recover all spell slots
            foreach (var slot in character.SpellSlots)
            {
                slot.Current = slot.Max;
            }

            if (character.Conditions.IsNullOrEmpty())
            {
                character.Conditions!.Clear();
            }

            character.DeathSavesFailures = 0;
            character.DeathSavesSuccesses = 0;

            await _repository.UpdateAsync(character);
            await SyncTabletopAsync(character);
            return true;
        }

        public async Task<BulkLongRestResult> BulkLongRestAsync(IEnumerable<string> characterIds)
        {
            var successfulIds = new List<string>();
            var failedIds = new List<string>();

            foreach (var id in characterIds)
            {
                var success = await LongRestAsync(id);
                if (success)
                {
                    successfulIds.Add(id);
                }
                else
                {
                    failedIds.Add(id);
                }
            }

            return new BulkLongRestResult
            {
                SuccessfulIds = successfulIds,
                FailedIds = failedIds
            };
        }
    }
}
