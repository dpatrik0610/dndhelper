using dndhelper.Models;
using dndhelper.Models.DTOs;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class LootService : ILootService
    {
        private const int MaxItems = 100;

        // Fixed drop rates out of 1000 rolls. The tier is rolled first, then an item inside it,
        // so a tier with 500 items is not more likely to drop than one with 5.
        public static readonly (string Tier, int Weight)[] TierRates =
        {
            ("Common", 500),
            ("Uncommon", 280),
            ("Rare", 140),
            ("Very Rare", 60),
            ("Legendary", 18),
            ("Artifact", 2),
        };

        private readonly IEquipmentService _equipmentService;
        private readonly IInventoryService _inventoryService;

        public LootService(IEquipmentService equipmentService, IInventoryService inventoryService)
        {
            _equipmentService = equipmentService;
            _inventoryService = inventoryService;
        }

        public async Task<Inventory> GenerateAsync(LootRequest request)
        {
            Guard.NotNull(request, nameof(request));
            Guard.NotNullOrWhiteSpace(request.Name, nameof(request.Name));

            var items = Roll(await _equipmentService.GetAllAsync(), request, Random.Shared);

            // InventoryService enforces that only the campaign's DM creates inventories.
            return await _inventoryService.CreateAsync(new Inventory
            {
                Name = request.Name.Trim(),
                CampaignId = request.CampaignId,
                CharacterIds = request.CharacterIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList(),
                Items = items,
            }) ?? throw new InvalidOperationException("Failed to create the loot inventory.");
        }

        /// <summary>Rolls the tiers first (fixed or DM-picked), then one matching item per tier slot.</summary>
        public static List<InventoryItem> Roll(IEnumerable<Equipment> equipment, LootRequest request, Random rng)
        {
            var pool = BuildPool(equipment, request.Tags);
            var tierCounts = request.TierCounts?.Where(kv => kv.Value > 0).ToList() ?? new();
            var tiers = tierCounts.Count > 0 ? PickedTiers(tierCounts, pool) : RandomTiers(request.Count, pool, rng);

            return Stack(tiers.Select(tier => PickOne(pool[tier], rng)));
        }

        /// <summary>Droppable items matching the tags, grouped by tier (case-insensitive).</summary>
        private static Dictionary<string, List<Equipment>> BuildPool(IEnumerable<Equipment> equipment, IEnumerable<string> tags)
        {
            var wanted = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return equipment
                .Where(e => !e.IsDeleted && !string.IsNullOrEmpty(e.Id))
                // A campaign's edited copy of a core item replaces it (same Index, ObjectId sorts after null).
                .GroupBy(e => e.Index ?? e.Id).Select(g => g.OrderByDescending(e => e.CampaignId).First())
                .Where(e => wanted.Count == 0 || (e.Tags ?? new()).Any(wanted.Contains))
                .GroupBy(TierOf, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        }

        private static string TierOf(Equipment e) => string.IsNullOrWhiteSpace(e.Tier) ? "Common" : e.Tier;

        /// <summary>DM-picked mode: each tier repeated as many times as asked.</summary>
        private static List<string> PickedTiers(List<KeyValuePair<string, int>> tierCounts, Dictionary<string, List<Equipment>> pool)
        {
            Guard.That(tierCounts.Sum(kv => kv.Value) <= MaxItems, $"Loot is capped at {MaxItems} items.", nameof(LootRequest.TierCounts));
            foreach (var tier in tierCounts.Select(kv => kv.Key).Where(tier => !pool.ContainsKey(tier)))
                throw new InvalidOperationException($"No {tier} items match the chosen tags.");

            return tierCounts.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        }

        /// <summary>Random mode: each slot rolls a tier at TierRates. Empty tiers leave the table; the rest keep their relative odds.</summary>
        private static List<string> RandomTiers(int count, Dictionary<string, List<Equipment>> pool, Random rng)
        {
            Guard.InRange(count, 1, MaxItems, nameof(LootRequest.Count));
            var rates = TierRates.Where(r => pool.ContainsKey(r.Tier)).ToList();
            if (rates.Count == 0) throw new InvalidOperationException("No items match the chosen tags.");

            return Enumerable.Range(0, count).Select(_ => RollTier(rates, rng)).ToList();
        }

        private static string RollTier(List<(string Tier, int Weight)> rates, Random rng)
        {
            var roll = rng.Next(rates.Sum(r => r.Weight));
            foreach (var (tier, weight) in rates)
            {
                if (roll < weight) return tier;
                roll -= weight;
            }
            throw new UnreachableException();
        }

        private static Equipment PickOne(List<Equipment> items, Random rng) => items[rng.Next(items.Count)];

        /// <summary>Repeated picks of the same item become one stack.</summary>
        private static List<InventoryItem> Stack(IEnumerable<Equipment> picks) =>
            picks.GroupBy(e => e.Id).Select(g => new InventoryItem
            {
                EquipmentId = g.Key,
                EquipmentName = g.First().Name,
                Quantity = g.Count(),
            }).ToList();
    }
}
