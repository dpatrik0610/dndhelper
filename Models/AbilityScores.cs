using MongoDB.Bson.Serialization.Attributes;

namespace dndhelper.Models
{
    public class AbilityScores
    {
        [BsonElement("str")]
        public int Str { get; set; } = 10;

        [BsonElement("dex")]
        public int Dex { get; set; } = 10;

        [BsonElement("con")]
        public int Con { get; set; } = 10;

        [BsonElement("int")]
        public int Int { get; set; } = 10;

        [BsonElement("wis")]
        public int Wis { get; set; } = 10;

        [BsonElement("cha")]
        public int Cha { get; set; } = 10;

    }
}
