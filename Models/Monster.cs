using dndhelper.Authorization;
using dndhelper.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    [BsonIgnoreExtraElements]
    public class Monster : IEntity, IOwnedResource
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public bool IsDeleted { get; set; } = false;
        [BsonElement("createdByUserId")] public string? CreatedByUserId { get; set; }
        [BsonElement("ownerId")] public List<string>? OwnerIds { get; set; } = new List<string>();
        [BsonElement("name")] public string? Name { get; set; }
        [BsonElement("isNpc")] public bool IsNpc { get; set; } = false;

        [JsonProperty("hp")]
        [BsonElement("hp")]
        public MonsterHP? HitPoints { get; set; }
        [BsonElement("size")] public List<string>? Size { get; set; } = new List<string>();
        [BsonSerializer(typeof(AlignmentListSerializer))] public List<List<string>> Alignment { get; set; } = new List<List<string>>();
        [BsonElement("speed")] public MonsterSpeed? Speed { get; set; }
        [BsonElement("cr")] public double? CR { get; set; }
        [BsonElement("languages")] public List<string>? Languages { get; set; } = new List<string>();
        [BsonSerializer(typeof(PassiveValueSerializer))] [BsonElement("passive")] private string? PassivePerception { get; set; }
        public int? Passive {
            get => GetCalculatedPassive();
            set => PassivePerception = value?.ToString();
        }
        [BsonElement("senses")] public List<string>? Senses { get; set; } = new List<string>();
        [BsonElement("source")] public string? Source { get; set; }
        [BsonElement("lore")] public string? Lore { get; set; }
        [BsonElement("abilityscores")] public AbilityScores AbilityScores { get; set; } = new AbilityScores();

        [BsonElement("type")]
        [BsonSerializer(typeof(MonsterTypeSerializer))]
        public MonsterType? Type { get; set; }


        // TODO: Actions, Spells, Savingthrows
        #region Armor Class
        [BsonIgnore]
        public List<int> ArmorClass { get; set; } = new List<int>();

        [BsonElement("ac")]
        [JsonProperty("ac")]
        private List<object> AcRaw
        {
            set
            {
                ArmorClass.Clear();
                if (value == null) return;

                foreach (var item in value)
                {
                    if (item is long l)
                        ArmorClass.Add((int)l);
                    else if (item is int i)
                        ArmorClass.Add(i);
                    else if (item is Newtonsoft.Json.Linq.JValue jval && jval.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                        ArmorClass.Add(jval.Value<int>());
                    // ignore objects
                }
            }
        }

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        #endregion


        public static int GetProficiencyBonusByCR(int cr)
        {
            if (cr <= 4) return 2;
            if (cr <= 8) return 3;
            if (cr <= 12) return 4;
            if (cr <= 16) return 5;
            if (cr <= 20) return 6;
            if (cr <= 24) return 7;
            if (cr <= 28) return 8;
            return 9;
        }

        public int GetCalculatedPassive()
        {
            if (string.IsNullOrWhiteSpace(PassivePerception))
                return 0;

            if (int.TryParse(PassivePerception, out var numeric))
                return numeric;

            // Example: "10 + (PB × 2)"
            if (PassivePerception.Contains("PB"))
            {
                var pb = GetProficiencyBonusByCR((int)CR!);
                return 10 + (pb * 2);
            }

            throw new InvalidOperationException($"Unsupported passive format: {PassivePerception}");
        }
    }

    [BsonIgnoreExtraElements]
    public class MonsterHP
    {
        [BsonElement("average")] public int? Average { get; set; } = null;
        [BsonElement("formula")] public string? Formula { get; set; } = null;
        [BsonElement("special")] public string? Special { get; set; } = null;
    }

    [BsonIgnoreExtraElements]
    public class MonsterSpeed
    {

        [BsonSerializer(typeof(MonsterSpeedSerializer))] [BsonElement("walk")] public int? Walk { get; set; }
        [BsonSerializer(typeof(MonsterSpeedSerializer))] [BsonElement("swim")] public int? Swim { get; set; }
        [BsonSerializer(typeof(MonsterSpeedSerializer))] [BsonElement("fly")] public int? Fly { get; set; }
        [BsonSerializer(typeof(MonsterSpeedSerializer))] [BsonElement("climb")] public int? Climb { get; set; }
        [BsonSerializer(typeof(MonsterSpeedSerializer))] [BsonElement("burrow")] public int? Burrow { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class MonsterType
    {
        [BsonElement("type")] public string? Type { get; set; }
        [BsonElement("tags")] public List<string>? Tags { get; set; }
    }
}