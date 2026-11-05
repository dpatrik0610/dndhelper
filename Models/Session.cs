using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace dndhelper.Models
{
    public class Session : IEntity, ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        public int Number { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Location { get; set; }

        [BsonRepresentation(BsonType.String)]
        public SessionStatus Status { get; set; } = SessionStatus.Planned;

        public DateTime? ScheduledFor { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public string? Plan { get; set; }

        [BsonElement("Description")]
        public string? Recap { get; set; }

        public List<SessionNote> Notes { get; set; } = new();
        public List<string> EncounterIds { get; set; } = new();

        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SessionStatus { Planned, Live, Completed }

    public class SessionNote
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Content { get; set; } = string.Empty;
        public bool DmOnly { get; set; }
        public string? Author { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    public class SessionNoteInput
    {
        public string Content { get; set; } = string.Empty;
        public bool DmOnly { get; set; }
    }
}
