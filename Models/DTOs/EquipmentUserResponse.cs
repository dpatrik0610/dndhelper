using System.Collections.Generic;

namespace dndhelper.Models.DTOs
{
    public class EquipmentUserResponse
    {
        public string? Id { get; set; } = null;
        public string Name { get; set; } = null!;
        public List<string>? Description { get; set; } = new List<string>();
        public Damage? Damage { get; set; }
        public Range? Range { get; set; }
        public double? Weight { get; set; }
        public List<string>? Tags { get; set; } = new List<string>();
        public string? Tier { get; set; } = "Common";
    }
}
