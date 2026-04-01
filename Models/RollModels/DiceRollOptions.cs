namespace dndhelper.Models.RollModels
{
    public class DiceRollOptions
    {
        public int MaxDice { get; set; } = 1000;
        public int MaxSides { get; set; } = 1000;
        public int PublicRateLimitMax { get; set; } = 20;
        public int PublicRateLimitWindowSeconds { get; set; } = 60;
        public int SubtleRateLimitMax { get; set; } = 3;
        public int SubtleRateLimitWindowSeconds { get; set; } = 60;
    }
}
