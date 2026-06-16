using dndhelper.Authorization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public class SellRequest : IEntity, IOwnedResource
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; } = null;

        [BsonRepresentation(BsonType.ObjectId)]
        public string CampaignId { get; set; } = null!;

        [BsonRepresentation(BsonType.ObjectId)]
        public string ShopId { get; set; } = null!;

        [BsonRepresentation(BsonType.ObjectId)]
        public string CharacterId { get; set; } = null!;

        [BsonRepresentation(BsonType.ObjectId)]
        public string EquipmentId { get; set; } = null!;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? SourceInventoryId { get; set; }

        public int Quantity { get; set; } = 1;
        public double OfferedPriceGp { get; set; }

        public bool IsSteal { get; set; } = false;

        [BsonRepresentation(BsonType.String)]
        public SellRequestStatus Status { get; set; } = SellRequestStatus.Pending;

        // Owners of this request (the player who made it + the DM/Campaign owners)
        [BsonRepresentation(BsonType.ObjectId)]
        public List<string> OwnerIds { get; set; } = new List<string>();

        // Metadata
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
    }
}
