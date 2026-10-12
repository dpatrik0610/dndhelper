using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    /// <summary>
    /// A stat block, flat and display-ready: rules text is plain text, numbers are numbers.
    /// Derived values (ability modifiers, proficiency bonus, XP) are computed by the client from these.
    /// </summary>
    public class Monster : IEntity, ICampaignContent
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

        public string Name { get; set; } = string.Empty;
        /// <summary>Book or ruleset, e.g. "MM", "SRD 5.1"; null for homebrew.</summary>
        public string? Source { get; set; }
        /// <summary>Stable id from the import ("srd-2014/goblin", "5etools/goblin|mm"); null for hand-made monsters.</summary>
        public string? SourceKey { get; set; }
        /// <summary>Named NPC rather than a generic creature.</summary>
        public bool IsNpc { get; set; }

        public string? Size { get; set; }
        public string? Type { get; set; }
        public List<string> Tags { get; set; } = new();
        public string? Alignment { get; set; }

        public int ArmorClass { get; set; }
        /// <summary>"natural armor", "chain mail, shield"...</summary>
        public string? ArmorNote { get; set; }
        public int HitPoints { get; set; }
        public string? HitDice { get; set; }
        public MonsterSpeed Speed { get; set; } = new();
        public AbilityScores AbilityScores { get; set; } = new();

        /// <summary>Total bonus by ability key ("dex": 6). Only proficient saves are listed.</summary>
        public Dictionary<string, int> SavingThrows { get; set; } = new();
        /// <summary>Total bonus by skill ("perception": 13).</summary>
        public Dictionary<string, int> Skills { get; set; } = new();

        public List<string> DamageVulnerabilities { get; set; } = new();
        public List<string> DamageResistances { get; set; } = new();
        public List<string> DamageImmunities { get; set; } = new();
        public List<string> ConditionImmunities { get; set; } = new();
        public List<string> Senses { get; set; } = new();
        public int PassivePerception { get; set; } = 10;
        public List<string> Languages { get; set; } = new();
        /// <summary>0.125, 0.25, 0.5, 1..30.</summary>
        public double ChallengeRating { get; set; }

        public List<MonsterFeature> Traits { get; set; } = new();
        public List<MonsterFeature> Actions { get; set; } = new();
        public List<MonsterFeature> BonusActions { get; set; } = new();
        public List<MonsterFeature> Reactions { get; set; } = new();
        public List<MonsterFeature> LegendaryActions { get; set; } = new();

        /// <summary>Lore / DM notes, markdown.</summary>
        public string? Description { get; set; }
        /// <summary>Public R2 URL (images/monsters); also used as the tabletop token art.</summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// 5etools imports are copyrighted book content kept for private use: they stay in the campaign
        /// they were imported into and are never moved to core or to another campaign.
        /// </summary>
        public static bool StaysInCampaign(Monster m) => m.SourceKey?.StartsWith("5etools/") == true;
    }

    public class MonsterSpeed
    {
        public int Walk { get; set; }
        public int? Fly { get; set; }
        public int? Swim { get; set; }
        public int? Climb { get; set; }
        public int? Burrow { get; set; }
        public bool Hover { get; set; }
    }

    /// <summary>A trait, action, reaction or legendary action. Description is plain text; lines are separated by \n.</summary>
    public class MonsterFeature
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
