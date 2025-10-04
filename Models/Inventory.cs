using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public class Inventory : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; } = null;             // MongoDB ObjectId as string

        [BsonRepresentation(BsonType.ObjectId)]
        public required string CharacterId { get; set; } = null!;    // Owner character's ObjectId as string

        public string Name { get; set; } = "Unnamed";       // Inventory name (e.g. "Chest", "Backpack")
        public List<InventoryItem>? Items { get; set; } = new();
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
    }

    public class InventoryItem
    {
        public string? EquipmentIndex { get; set; } // Reference to Equipment.Index
        public int? Quantity { get; set; } = 1;              // How many of this item
        public string? Note { get; set; } = string.Empty;                  // Optional notes (e.g. "Enchanted", "Broken")
    }
}
