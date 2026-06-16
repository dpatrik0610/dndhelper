using dndhelper.Authorization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public class Shop : IEntity, IOwnedResource
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; } = null;

        [BsonRepresentation(BsonType.ObjectId)]
        public string CampaignId { get; set; } = null!;

        [BsonRepresentation(BsonType.ObjectId)]
        public string InventoryId { get; set; } = null!;

        [BsonRepresentation(BsonType.ObjectId)]
        public List<string> OwnerIds { get; set; } = new List<string>();

        public string Name { get; set; } = "New Shop";
        public string? Description { get; set; }

        public bool IsOpened { get; set; } = false;

        // Custom pricing factor (e.g., 1.0 = standard cost, 1.2 = 20% markup, 0.8 = 20% discount)
        public double PriceMultiplier { get; set; } = 1.0;

        // Metadata
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
    }
}
