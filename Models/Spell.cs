using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public class Spell : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("index")]
        public string? Index { get; set; }

        [BsonElement("name")]
        public string? Name { get; set; }

        [BsonElement("desc")]
        public List<string>? Description { get; set; } = new List<string>();

        [BsonElement("higher_level")]
        public List<string>? HigherLevel { get; set; } = new List<string>();

        [BsonElement("range")]
        public string? Range { get; set; }

        [BsonElement("components")]
        public List<string>? Components { get; set; } = new List<string>();

        [BsonElement("material")]
        public string? Material { get; set; }

        [BsonElement("ritual")]
        public bool? Ritual { get; set; }

        [BsonElement("duration")]
        public string? Duration { get; set; }

        [BsonElement("concentration")]
        public bool? Concentration { get; set; }

        [BsonElement("casting_time")]
        public string? CastingTime { get; set; }

        [BsonElement("level")]
        public int? Level { get; set; }

        [BsonElement("attack_type")]
        public string? AttackType { get; set; }

        [BsonElement("damage")]
        public SpellDamage? Damage { get; set; }

        [BsonElement("dc")]
        public DifficultyClass? DC { get; set; }

        [BsonElement("area_of_effect")]
        public AreaOfEffect? AreaOfEffect { get; set; }

        [BsonElement("school")]
        public School? School { get; set; }

        [BsonElement("classes")]
        public List<Class>? Classes { get; set; } = new List<Class>();

        [BsonElement("subclasses")]
        public List<Subclass>? Subclasses { get; set; } = new List<Subclass>();

        [BsonElement("heal_at_slot_level")]
        public Dictionary<string, string>? HealAtSlotLevel { get; set; } = new Dictionary<string, string>();


        [BsonElement("spell_url")]
        public string? SpellUrl { get; set; }
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;

        [BsonElement("updated_at")]
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SpellDamage
    {
        [BsonElement("damage_type")]
        public SpellDamageType? DamageType { get; set; }

        [BsonElement("damage_at_slot_level")]
        public Dictionary<string, string>? DamageAtSlotLevel { get; set; } = new Dictionary<string, string>();

        [BsonElement("damage_at_character_level")]
        public Dictionary<string, string>? DamageAtCharacterLevel { get; set; } = new Dictionary<string, string>();
    }

    public class SpellDamageType
    {
        [BsonElement("name")]
        public string? Name { get; set; }
    }

    public class DifficultyClass
    {
        [BsonElement("dc_type")]
        public DcType? DcType { get; set; }

        [BsonElement("dc_success")]
        public string? DcSuccess { get; set; }

        [BsonElement("desc")]
        public string? Description { get; set; }
    }

    public class DcType
    {
        [BsonElement("name")]
        public string? Name { get; set; }
    }

    public class AreaOfEffect
    {
        [BsonElement("type")]
        public string? Type { get; set; }
        [BsonElement("size")]
        public int? Size { get; set; }
    }

    public class School
    {
        [BsonElement("name")]
        public string? Name { get; set; }
    }

    public class Class
    {
        [BsonElement("name")]
        public string? Name { get; set; }
    }

    public class Subclass
    {
        [BsonElement("name")]
        public string? Name { get; set; }
    }
}
