namespace dndhelper.Models.CharacterModels
{
    /// <summary>Any limited-use class pool: Ki, Sorcery Points, Rage, or a custom one.</summary>
    public class ClassResource
    {
        public string Name { get; set; } = string.Empty;
        public int Current { get; set; } = 0;
        public int Max { get; set; } = 0;
        public string Recharge { get; set; } = "long"; // "short" | "long" | "none"
    }
}
