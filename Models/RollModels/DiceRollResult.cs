using System;
using System.Collections.Generic;

namespace dndhelper.Models.RollModels
{
    public class DiceRollResult
    {
        public int NumberOfDice { get; set; }
        public int Sides { get; set; }
        public int Modifier { get; set; }
        public List<int> Rolls { get; set; } = new();
        public int Total { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public double Average { get; set; }
        public string Expression { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
    }
}
