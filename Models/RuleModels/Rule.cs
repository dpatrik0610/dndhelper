using dndhelper.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models.RuleModels
{
    public class Rule : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRequired]
        [BsonElement("slug")]
        public string Slug { get; set; } = default!;

        [BsonRequired]
        [BsonElement("title")]
        public string Title { get; set; } = default!;

        [BsonRequired]
        [BsonElement("category")]
        public string Category { get; set; } = default!;

        [BsonRequired]
        [BsonElement("summary")]
        public string Summary { get; set; } = default!;

        [BsonRequired]
        [BsonElement("tags")]
        public List<string> Tags { get; set; } = new();

        [BsonElement("updatedAt")]
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("source")]
        public RuleSource? Source { get; set; }

        [BsonElement("body")]
        public List<string>? Body { get; set; } = new();

        [BsonElement("sources")]
        public List<RuleSource>? Sources { get; set; } = new();

        [BsonElement("examples")]
        public List<RuleExample>? Examples { get; set; } = new();

        [BsonElement("references")]
        public List<RuleReference>? References { get; set; } = new();

        [BsonElement("relatedRuleSlugs")]
        public List<string>? RelatedRuleSlugs { get; set; } = new();

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsDeleted { get; set; }
    }

    public class RuleSource
    {
        [BsonElement("title")]
        public string? Title { get; set; }

        [BsonElement("page")]
        public string? Page { get; set; }

        [BsonElement("edition")]
        public string? Edition { get; set; }

        [BsonElement("url")]
        public string? Url { get; set; }
    }

    public class RuleExample
    {
        [BsonElement("title")]
        public string? Title { get; set; }

        [BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        [BsonElement("outcome")]
        public string? Outcome { get; set; }
    }

    public class RuleReference
    {
        [BsonElement("type")]
        public string Type { get; set; } = string.Empty;

        [BsonElement("id")]
        public string? Id { get; set; }

        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("url")]
        public string? Url { get; set; }
    }
}
