using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace dndhelper.Models.EncounterRoomModels
{
    public class SessionEntity
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The userId who controls this entity. A player can own multiple entities.
        /// This is NOT a characterId — it maps to the user's account/JWT identity.
        /// </summary>
        public string? OwnerId { get; set; }

        public bool IsPlayer { get; set; }
        public bool IsVisible { get; set; } = true;
        public int? Initiative { get; set; }
        public string Color { get; set; } = "#FFFFFF";

        /// <summary>
        /// Flexible attribute bag for stats like MaxHp, CurrentHp, AC, Condition, etc.
        /// Example: { "MaxHp": 45, "CurrentHp": 32, "AC": 16, "Condition": "Stunned" }
        /// </summary>
        [BsonExtraElements]
        public BsonDocument Attributes { get; set; } = new();
    }
}
