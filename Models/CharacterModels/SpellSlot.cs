namespace dndhelper.Models.CharacterModels
{
    public class SpellSlot
    {
        public int Level { get; set; }           // Spell level (1-9)
        public int Current { get; set; } = 0;    // Currently available slots
        public int Max { get; set; } = 0;        // Maximum slots for that level
    }
}
