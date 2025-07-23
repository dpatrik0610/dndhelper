using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace dndhelper.Authentication
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
    }
}
