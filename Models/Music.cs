using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public static class MusicKinds
    {
        public const string Inbox = "inbox";
        public const string Situation = "situation";
    }

    public class MusicCollection : ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        public string Kind { get; set; } = MusicKinds.Situation;
        public string Name { get; set; } = string.Empty;
        public int Order { get; set; }
    }

    public class MusicTrack : ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string CollectionId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string? Artist { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string FileId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CoverFileId { get; set; }

        public string Hash { get; set; } = string.Empty;
        public double Duration { get; set; }
        public int Order { get; set; }
    }

    public class AmbienceLayer : ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string CollectionId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string FileId { get; set; } = string.Empty;

        public double Level { get; set; } = 0.6;
        public bool On { get; set; } = true;
        public int Order { get; set; }
    }

    public class MusicVote
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Kind { get; set; } = "next";
        public string By { get; set; } = string.Empty;
        public string ByName { get; set; } = string.Empty;
        public string Track { get; set; } = string.Empty;
        public int Index { get; set; }
        public int Eligible { get; set; }
        public List<string> Yes { get; set; } = new();
        public List<string> No { get; set; } = new();
        public string Status { get; set; } = "open";
        public long StartedAt { get; set; }
        public long EndsAt { get; set; }
        public long? ClosedAt { get; set; }
        public string? DecidedBy { get; set; }
    }

    public class MusicPlayer : ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        public List<string> Queue { get; set; } = new();
        public List<string> Order { get; set; } = new();
        public int Index { get; set; }
        public bool Playing { get; set; }
        public long StartedAt { get; set; }
        public double Position { get; set; }
        public bool Shuffle { get; set; }
        public string Repeat { get; set; } = "off";
        public long Rev { get; set; }

        public double Crossfade { get; set; }
        public string Easing { get; set; } = "inOut";
        public bool SoftPause { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? AmbienceCollectionId { get; set; }

        public MusicVote? Vote { get; set; }
    }
}
