using System.Collections.Generic;

namespace dndhelper.Models
{
    public class MonsterSearchCriteria
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public double? MinCR { get; set; }
        public double? MaxCR { get; set; }
        public List<string>? Tags { get; set; }
        public string SortBy { get; set; } = "Name";
        public bool SortDescending { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
