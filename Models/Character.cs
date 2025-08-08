using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace dndhelper.Models
{
    public class Character
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Lore { get; set; }
        public int Age { get; set; }
        public string Race { get; set; } = "Unknown Race";
        public string CharacterClass { get; set; } = "Human";
    }
}
