using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models.CharacterModels
{
    public class Character : IEntity
    {
        #region METADATA
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public string? OwnerId { get; set; }
        public string? CampaignId { get; set; }
        public string? ImageUrl { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

        #endregion

        #region BASIC INFO
        public string? Name { get; set; } = "Unknown Traveler";
        public bool? IsDead { get; set; } = false;
        public bool? IsNPC { get; set; } = false;
        public string? Race { get; set; } = "Human";
        public string? CharacterClass { get; set; } = "Fighter";
        public string? Background { get; set; } = "Folk Hero";
        public int? Level { get; set; } = 1;
        public int? ArmorClass { get; set; } = 10;

        public int? HitPoints { get; set; } = 10;
        public int? MaxHitPoints { get; set; } = 10;
        public int? TemporaryHitPoints { get; set; } = 0;

        public int? Speed { get; set; } = 30;
        public int? Initiative { get; set; } = 0;
        public string? Alignment { get; set; } = "Neutral";
        public int? ProficiencyBonus { get; set; } = 2;

        public List<string>? Proficiencies { get; set; } = new List<string>();
        public List<string>? Languages { get; set; } = new List<string> { "Common" };
        public List<string>? Conditions { get; set; } = new List<string>(); // Later can be enum
        public List<string>? Resistances { get; set; } = new List<string>(); // Later can be enum
        public List<string>? Immunities { get; set; } = new List<string>(); // Later can be enum
        public List<string>? Vulnerabilities { get; set; } = new List<string>(); // Later can be enum
        public List<string>? Features { get; set; } = new List<string>();
        public List<string>? Actions { get; set; } = new List<string>();
        public List<string>? Spells { get; set; } = new List<string>(); // Later can be more complex Spell class

        public List<Currency>? Currencies { get; set; } = new List<Currency>
        {
            new Currency { Type = "Silver", Amount = 0, CurrencyCode = "sp" },
            new Currency { Type = "Gold", Amount = 0, CurrencyCode = "gp"},
        };

        #endregion

        #region STATBLOCK
        public AbilityScores? AbilityScores { get; set; } = new AbilityScores();
        public SavingThrows? SavingThrows { get; set; } = new SavingThrows();
        public int? Inspiration { get; set; } = 0;
        public Dictionary<string, int>? Skills { get; set; } = new Dictionary<string, int> // Maybe updatable with a class or enum later?
        {
            { "Acrobatics", 0 },
            { "Animal Handling", 0 },
            { "Arcana", 0 },
            { "Athletics", 0 },
            { "Deception", 0 },
            { "History", 0 },
            { "Insight", 0 },
            { "Intimidation", 0 },
            { "Investigation", 0 },
            { "Medicine", 0 },
            { "Nature", 0 },
            { "Perception", 0 },
            { "Performance", 0 },
            { "Persuasion", 0 },
            { "Religion", 0 },
            { "Sleight of Hand", 0 },
            { "Stealth", 0 },
            { "Survival", 0 },
            // Custom skills can be added later
            { "Willpower", 0 }
        };
        public int? SpellSaveDc { get; set; } = 10;
        public int? SpellAttackBonus { get; set; } = 0;

        public int? DeathSavesSuccesses { get; set; } = 0;
        public int? DeathSavesFailures { get; set; } = 0;

        public int? PassivePerception { get; set; } = 10;
        public int? PassiveInvestigation { get; set; } = 10;
        public int? PassiveInsight { get; set; } = 10;

        public int? Experience { get; set; } = 0;
        public int? CarryingCapacity { get; set; } = 0;
        public int? CurrentEncumbrance { get; set; } = 0;

        #endregion

        #region FILLER
        public List<string>? Backstory { get; set; } = new List<string>();
        public string? Size { get; set; } = HeightLabel.MEDIUM;
        public int? Age { get; set; } = 25;
        public string? Height { get; set; } = "5'8\"";
        public string? Weight { get; set; } = "150 lbs";
        public string? Eyes { get; set; } = "Brown";
        public string? Skin { get; set; } = "Light";
        public string? Hair { get; set; } = "Black";
        public string? Appearance { get; set; } = "A nondescript individual.";
        public string? PersonalityTraits { get; set; } = "Brave and loyal.";
        public string? Ideals { get; set; } = "Justice and honor.";
        public string? Bonds { get; set; } = "Family and friends.";
        public string? Flaws { get; set; } = "Impulsive and stubborn.";
        public string? Notes { get; set; } = "No additional notes.";
        public string? Description { get; set; } = "A brave adventurer.";
        #endregion

        #region COLLECTIONS

        public List<string>? FactionIds { get; set; } = new List<string>();
        public List<string>? InventoryIds { get; set; } = new List<string>();
        public string EquipmentId { get; set; } = string.Empty;
        public bool IsDeleted { get; set; } = false;

        #endregion
    }
}
