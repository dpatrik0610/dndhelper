using System.Collections.Generic;

namespace dndhelper.Models
{
    public class Faction
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<string>? MemberIds { get; set; }
        public List<string>? AllyFactionIds { get; set; }
        public List<string>? EnemyFactionIds { get; set; }
        public string? LeaderId { get; set; }
        public string? HeadquartersLocation { get; set; }
        public List<string>? Goals { get; set; }
        public List<string>? Resources { get; set; }

    }
}
