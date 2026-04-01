using dndhelper.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models.RollModels
{
    public enum RollType
    {
        Public = 0,
        Subtle = 1
    }

    public class RollRecord : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CharacterId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        public RollType Type { get; set; }
        public string Expression { get; set; } = string.Empty;
        public int NumberOfDice { get; set; }
        public int Sides { get; set; }
        public int Modifier { get; set; }
        public List<int> Rolls { get; set; } = new();
        public int Total { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public double Average { get; set; }
        public string? Note { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
    }
}
