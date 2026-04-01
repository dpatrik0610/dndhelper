using System.Collections.Generic;

namespace dndhelper.Models.CampaignModels
{
    public class CampaignOverviewDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<CampaignCharacterDto> Characters { get; set; } = new();
        public List<string>? OwnerIds { get; set; } = new List<string>();
        public string? CurrentSessionId { get; set; }
        public List<string> QuestIds { get; set; } = new();
    }

    public class CampaignCharacterDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public bool? IsDead { get; set; }
        public bool? IsNPC { get; set; }
    }
}
