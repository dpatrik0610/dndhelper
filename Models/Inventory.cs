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
        public string? Id { get; set; } = null;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CharacterId { get; set; } = null;

        public string Name { get; set; } = "Unnamed";
        public List<InventoryItem>? Items { get; set; } = new();
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
    }

    public class InventoryItem
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string? EquipmentId { get; set; }
        public string? EquipmentName { get; set; }
        public int? Quantity { get; set; } = 1;
    }
}
