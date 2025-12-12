using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace dndhelper.Models.CharacterModels
{
    public class CharacterSpell
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string? SpellId { get; set; } = string.Empty;

        public bool IsPrepared { get; set; } = false;
    }
}
