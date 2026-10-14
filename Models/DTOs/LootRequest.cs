using System.Collections.Generic;

namespace dndhelper.Models.DTOs
{
    public class LootRequest
    {
        /// <summary>Name of the inventory the loot is put into.</summary>
        public string Name { get; set; } = "Loot";
        public string? CampaignId { get; set; }
        public List<string> CharacterIds { get; set; } = new();

        /// <summary>Random mode: how many items to roll; each one rolls its tier first.</summary>
        public int Count { get; set; }

        /// <summary>DM-picked mode: exact item count per tier (e.g. {"Rare": 2}). When it has any entry, Count is ignored.</summary>
        public Dictionary<string, int>? TierCounts { get; set; }

        /// <summary>Only items carrying at least one of these tags. Empty means any item.</summary>
        public List<string> Tags { get; set; } = new();
    }
}
