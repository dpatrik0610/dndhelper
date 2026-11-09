using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace dndhelper.Models
{
    public class CritterCatch
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Critter { get; set; } = string.Empty;
        public DateTime CaughtAt { get; set; } = DateTime.UtcNow;
    }
}
