using System.Collections.Generic;

namespace dndhelper.Models.DTOs
{
    public class ShopItemResponse
    {
        public string EquipmentId { get; set; } = null!;
        public string EquipmentName { get; set; } = null!;
        public int Quantity { get; set; }
        public string? Note { get; set; }
        
        // Calculated final cost using the shop's multiplier
        public int FinalCostSp { get; set; }
        public string DisplayCost { get; set; } = null!;
        
        // Basic equipment info safe for players to see
        public List<string>? Description { get; set; }
        public Damage? Damage { get; set; }
        public Range? Range { get; set; }
        public double? Weight { get; set; }
        public List<string>? Tags { get; set; }
        public string? Tier { get; set; }
    }
}