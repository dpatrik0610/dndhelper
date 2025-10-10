using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public class Campaign : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        // Ownership and participants
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string DungeonMasterId { get; set; } = string.Empty;
        public List<string> PlayerIds { get; set; } = new();

        // Related entities
        public List<string> WorldIds { get; set; } = new();
        public List<string> QuestIds { get; set; } = new();
        public List<string> NoteIds { get; set; } = new();

        // Metadata
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsActive { get; set; } = true;

        // Session tracking (for later use)
        public string? CurrentSessionId { get; set; }
        public List<string> SessionIds { get; set; } = new();
    }
}
