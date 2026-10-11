using System.Collections.Generic;

namespace dndhelper.Models.CharacterModels
{
    /// <summary>
    /// Something applied on top of the base sheet (equipped item, potion, spell...). The base stats are never changed;
    /// the frontend layers active effects over them and shows which effect caused each change.
    /// </summary>
    public class CharacterEffect
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>item | potion | spell | feature | custom</summary>
        public string Source { get; set; } = "custom";
        /// <summary>Set when the effect comes from equipping an inventory item.</summary>
        public string? EquipmentId { get; set; }
        /// <summary>Added because the item is in the character's "Equipment" inventory; removed when it leaves.</summary>
        public bool Auto { get; set; }
        public bool Active { get; set; } = true;
        public string? Note { get; set; }
        public List<StatModifier> Modifiers { get; set; } = new List<StatModifier>();
    }

    public class StatModifier
    {
        /// <summary>e.g. "con", "save:dex", "saves", "skill:Stealth", "checks", "attack", "ac", "speed", "initiative", "maxHp".</summary>
        public string Target { get; set; } = string.Empty;
        /// <summary>bonus | set | min | advantage | disadvantage | proficiency | note</summary>
        public string Type { get; set; } = "bonus";
        public int? Value { get; set; }
        public string? Text { get; set; }
        /// <summary>Only counts while this situation (e.g. "Swamp") is switched on for the character; empty = always.</summary>
        public string? When { get; set; }
    }
}
