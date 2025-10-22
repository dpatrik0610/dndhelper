using dndhelper.Models.CharacterModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.CharacterServices.Interfaces;
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
        public CharacterService(ICharacterRepository repository, ILogger logger, IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor)
            : base(repository, logger, authorizationService, httpContextAccessor) { }

        public Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId) 
            => _repository.GetByOwnerIdAsync(ownerId);
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
            return true;
        }
    }
}
