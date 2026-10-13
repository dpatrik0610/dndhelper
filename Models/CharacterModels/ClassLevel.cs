namespace dndhelper.Models.CharacterModels
{
    /// <summary>A class taken on top of the main one when multiclassing.</summary>
    public class ClassLevel
    {
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; } = 1;
        /// <summary>Hit die sides (8 = d8).</summary>
        public int HitDie { get; set; } = 8;
    }
}
