using dndhelper.Authorization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public class SellRequest : IEntity, IOwnedResource, ICampaignScoped
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
        public string? EquipmentId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? SourceInventoryId { get; set; }

        public int Quantity { get; set; } = 1;
        public double OfferedPriceGp { get; set; }

        public bool IsSteal { get; set; } = false;
        public bool IsHaggle { get; set; } = false;
        public double? CounterPriceGp { get; set; }

        public bool IsTrade { get; set; } = false;
        public List<TradeItem> GiveItems { get; set; } = new List<TradeItem>();
        public List<TradeItem> WantItems { get; set; } = new List<TradeItem>();

        [BsonRepresentation(BsonType.String)]
        public SellRequestStatus Status { get; set; } = SellRequestStatus.Pending;

        // Owners of this request (the player who made it + the DM/Campaign owners)
        [BsonRepresentation(BsonType.ObjectId)]
        public List<string> OwnerIds { get; set; } = new List<string>();

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
    }
}

namespace dndhelper.Models
{
    public class TradeItem
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string EquipmentId { get; set; } = null!;
        public string? EquipmentName { get; set; }
        public int Quantity { get; set; } = 1;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? SourceInventoryId { get; set; }
    }
}
