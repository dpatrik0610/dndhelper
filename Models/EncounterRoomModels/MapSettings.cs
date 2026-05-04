using System.Text.Json.Serialization;

namespace dndhelper.Models.EncounterRoomModels
{
    public class MapSettings
    {
        public string? MapImageUrl { get; set; }
        public GridType GridType { get; set; } = GridType.Square;
        public double GridCellSize { get; set; } = 50;
        public int GridWidth { get; set; } = 20;
        public int GridHeight { get; set; } = 20;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GridType
    {
        Square,
        Hex
    }
}
