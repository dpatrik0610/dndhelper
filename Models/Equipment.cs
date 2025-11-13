using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
namespace dndhelper.Models
{
    public class Equipment : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; } = null;
        public string Index { get; set; } = null!;
        public string Name { get; set; } = null!;
        [JsonProperty("desc")]
        public List<string>? Description { get; set; } = new List<string>();
        [JsonProperty("dmdesc")]
        public List<string>? DmDescription { get; set; } = new List<string>();
        [JsonProperty("cost")]
        public Cost? Cost { get; set; }
        [JsonProperty("damage")]
        public Damage? Damage { get; set; }
        [JsonProperty("range")]
        public Range? Range { get; set; }
        [JsonProperty("weight")]
        public double? Weight { get; set; }
        public bool IsCustom { get; set; }
        public DateTime? CreatedAt { get;set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        public List<string>? Tags { get; set; } = new List<string>();
        public string? Tier { get; set; } = "Common";
    }

    public class Cost
    {
        [JsonProperty("quantity")]
        public int Quantity { get; set; }
        [JsonProperty("unit")]
        public string Unit { get; set; } = null!;
    }

    public class Damage
    {
        [JsonProperty("damage_dice")]
        public string DamageDice { get; set; } = null!;
        [JsonProperty("damage_type")]
        public DamageType DamageType { get; set; } = null!;
    }
    public class DamageType
    {
        [JsonProperty("name")]
        public string Name { get; set; } = null!;
    }

    public class Range
    {
        [JsonProperty("normal")]
        public int Normal { get; set; }
        [JsonProperty("long")]
        public int Long { get; set; }
    }

}
