using System.Collections.Generic;

namespace dndhelper.Models.RuleModels
{
    public class RuleSnippetDto
    {
        public string? Id { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public string? UpdatedAt { get; set; }
        public RuleSource? Source { get; set; }
    }

    public class RuleDetailDto : RuleSnippetDto
    {
        public List<string>? Body { get; set; } = new();
        public List<RuleSource>? Sources { get; set; } = new();
        public List<RuleExample>? Examples { get; set; } = new();
        public List<RuleReference>? References { get; set; } = new();
        public List<string>? RelatedRuleSlugs { get; set; } = new();
    }

    public class RuleListResponse
    {
        public List<RuleSnippetDto> Items { get; set; } = new();
        public long Total { get; set; }
        public string? NextCursor { get; set; }
    }

    public class RuleDetailResponse
    {
        public RuleDetailDto Rule { get; set; } = new();
    }

    public class RuleQueryOptions
    {
        public string? Category { get; set; }
        public string? Tag { get; set; }
        public string? Source { get; set; }
        public string? Search { get; set; }
        public string? Cursor { get; set; }
        public int Limit { get; set; } = 20;
    }

    public class RuleQueryResult
    {
        public List<Rule> Items { get; set; } = new();
        public long Total { get; set; }
        public string? NextCursor { get; set; }
    }

    public class RuleStats
    {
        public Dictionary<string, long> ByCategory { get; set; } = new();
        public List<RuleTagCount> TopTags { get; set; } = new();
        public long Total { get; set; }
    }

    public class RuleTagCount
    {
        public string Tag { get; set; } = string.Empty;
        public long Count { get; set; }
    }
}
