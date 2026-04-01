using System;
using System.Collections.Generic;

namespace dndhelper.Models.RollModels
{
    public class SubtleRollRequest
    {
        public string CharacterId { get; set; } = string.Empty;
        public string? Expression { get; set; }
        public int NumberOfDice { get; set; }
        public int Sides { get; set; }
        public string? Note { get; set; }
    }

    public class SubtleRollReceipt
    {
        public string RollId { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
        public int DmCount { get; set; }
    }

    public class SubtleRollPayload
    {
        public string RollId { get; set; } = string.Empty;
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RolledByUserId { get; set; } = string.Empty;
        public string RolledByUsername { get; set; } = string.Empty;
        public int NumberOfDice { get; set; }
        public int Sides { get; set; }
        public int Modifier { get; set; }
        public List<int> Rolls { get; set; } = new();
        public int Total { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public double Average { get; set; }
        public string Expression { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime TimestampUtc { get; set; }
    }
}
