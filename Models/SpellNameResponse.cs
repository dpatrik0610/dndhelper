using MongoDB.Bson.Serialization.Attributes;

namespace dndhelper.Models
{
    public class SpellNameResponse
    {

        [BsonId]
        [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
        public string? Id { get; set; }
        [BsonElement("name")]
        public string? Name { get; set; } = null;
        [BsonElement("level")]
        public int? Level { get; set; } = null;
        [BsonElement("school")]
        public SpellName? School { get; set; }
    }

    public class SpellName
    {
        [BsonElement("name")]
        public string? Name { get; set; } = null;
    }
}
