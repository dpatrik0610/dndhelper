using MongoDB.Bson;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace dndhelper.Models.EncounterRoomModels
{
    public class MapElement
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public MapElementType Type { get; set; }
        public ShapeType? Shape { get; set; }
        public List<Point2D> Points { get; set; } = new();
        public string Color { get; set; } = "#FFFFFF";
        public int Thickness { get; set; } = 2;
        public bool IsVisible { get; set; } = true;
        public string CreatedById { get; set; } = string.Empty;
    }

    public class Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MapElementType
    {
        Drawing,
        Shape,
        Marker,
        Ruler,
        Text
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShapeType
    {
        Circle,
        Square,
        Rectangle,
        Cone,
        Line,
        Cube,
        Sphere
    }
}
