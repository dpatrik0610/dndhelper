using MongoDB.Bson;

namespace dndhelper.Models.EncounterRoomModels
{
    public class RoomToken
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string EntityId { get; set; } = string.Empty;
        public Point2D Position { get; set; } = new();
        public int Size { get; set; } = 1;
        public bool Hidden { get; set; }
        public string? ImageUrl { get; set; }
    }
}
