using System.Collections.Generic;

namespace dndhelper.Models.DTOs
{
    /// <summary>One row of the hidden critter scoreboard.</summary>
    public class CritterScoreDto
    {
        public string Username { get; set; } = string.Empty;
        public int Total { get; set; }
        public Dictionary<string, int> Caught { get; set; } = new Dictionary<string, int>();
    }
}
