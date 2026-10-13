using dndhelper.Models;
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
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace dndhelper.Services
{

    public class CharacterService : BaseService<Character, ICharacterRepository> , ICharacterService
    {
        private readonly ITabletopService _tabletop;
        private readonly IUserRepository _users;
        private readonly IInventoryRepository _inventories;
        private readonly IFileStorageService _storage;

        public CharacterService(ICharacterRepository repository, ILogger logger, IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor, ITabletopService tabletop, IUserRepository users, IInventoryRepository inventories,
        IFileStorageService storage)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _tabletop = tabletop;
            _users = users;
            _inventories = inventories;
            _storage = storage;
        }

        /// <summary>
        /// Old uploads are kept: saved encounter boards may still show them. If saving the character fails,
        /// the new upload is removed again (ReplaceAsync), so nothing is left unreferenced.
        /// </summary>
        public async Task<Character> UploadImageAsync(string characterId, System.IO.Stream content, string fileName)
        {
            var character = await _repository.GetByIdAsync(characterId) ?? throw new NotFoundException("Character not found.");
            await EnsureWriteAccess(character);
            var userId = GetCurrentUserId() ?? throw new UnauthorizedAccessException("Not signed in.");

            Character? saved = null;
            await _storage.ReplaceAsync(null, content, StorageFolder.Tokens, userId, async file =>
            {
                character.ImageUrl = file.Url ?? throw new InvalidOperationException("Token images need a public R2 domain (R2__PublicBaseUrl).");
                saved = await UpdateAsync(character) ?? throw new InvalidOperationException("Could not save the character.");
            }, fileName);
            return saved!;
        }

        /// <summary>
        /// A new character is listed on every owner's account (the creator is added as owner by the base) and
        /// gets its starting inventory here, so players never need permission to create inventories.
        /// </summary>
        public override async Task<Character?> CreateAsync(Character entity)
        {
            var created = await base.CreateAsync(entity);
            if (created?.Id == null) return created;

            foreach (var ownerId in (created.OwnerIds ?? new List<string>()).Distinct())
                await _users.AddCharacterIdAsync(ownerId, created.Id);

            var inventory = await _inventories.CreateAsync(new Inventory
            {
                Name = $"{created.Name}'s Equipment",
                CharacterIds = new List<string> { created.Id },
                OwnerIds = created.OwnerIds ?? new List<string>(),
                CampaignId = created.CampaignId,
            });
            if (inventory?.Id != null)
            {
                created.InventoryIds = new List<string> { inventory.Id };
                created = await _repository.UpdateAsync(created) ?? created;
            }
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

            RegainHitDice(character);

            // Short-rest pools also refill on a long rest
            foreach (var resource in character.ClassResources ?? new List<ClassResource>())
            {
                if (resource.Recharge != "none") resource.Current = resource.Max;
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

        /// <summary>5e: a long rest gives back spent hit dice up to half the total (at least one), biggest dice first.</summary>
        public static void RegainHitDice(Character character)
        {
            var spent = character.HitDiceSpent ?? new Dictionary<string, int>();
            var total = Regex.Matches(character.HitDice ?? "", @"(\d*)\s*d\s*(\d+)", RegexOptions.IgnoreCase)
                .Sum(m => m.Groups[1].Value == "" ? 1 : int.Parse(m.Groups[1].Value));
            var regain = Math.Max(1, total / 2);
            foreach (var die in spent.Keys.OrderByDescending(k => int.TryParse(k.TrimStart('d'), out var sides) ? sides : 0).ToList())
            {
                var back = Math.Min(Math.Max(spent[die], 0), regain);
                spent[die] -= back;
                regain -= back;
                if (spent[die] <= 0) spent.Remove(die);
            }
            character.HitDiceSpent = spent;
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
