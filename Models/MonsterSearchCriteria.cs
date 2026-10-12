namespace dndhelper.Models
{
    public class MonsterSearchCriteria
    {
        /// <summary>Matches name, type or tags.</summary>
        public string? Query { get; set; }
        public string? Type { get; set; }
        public double? MinCr { get; set; }
        public double? MaxCr { get; set; }
        public bool? IsNpc { get; set; }
        /// <summary>"name" (default) or "cr".</summary>
        public string? SortBy { get; set; }
        public bool Desc { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }
}
