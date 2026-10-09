using dndhelper.Models.RollModels;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace dndhelper.Models
{
    /// <summary>
    /// Virtual tabletop state for one campaign: grid, tokens (which double as initiative rows),
    /// turn order, drawings, AoE templates and the shared roll log.
    /// </summary>
    public class Tabletop : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string CampaignId { get; set; } = string.Empty;

        public string JoinCode { get; set; } = NewJoinCode();

        public GridSettings Grid { get; set; } = new();
        public MapLayer Map { get; set; } = new();
        public FogState Fog { get; set; } = new();
        public List<TableToken> Tokens { get; set; } = new();
        public TableTurn Turn { get; set; } = new();
        public List<TableStroke> Strokes { get; set; } = new();
        public List<AoeTemplate> Templates { get; set; } = new();
        public List<TableLogEntry> Log { get; set; } = new();

        /// <summary>The campaign Encounter currently being played on this table, if any.</summary>
        public TableEncounterInfo? Encounter { get; set; }

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; }

        /// <summary>6 uppercase chars, no O/0/1/I to avoid confusion when read aloud.</summary>
        public static string NewJoinCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            return RandomNumberGenerator.GetString(chars, 6);
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GridType
    {
        Square,
        HexPointy,
        HexFlat
    }

    public class GridSettings
    {
        public GridType Type { get; set; } = GridType.Square;
        /// <summary>One cell (5 ft) in world pixels. For hex grids this is the flat-to-flat width.</summary>
        public int CellSize { get; set; } = 70;
        /// <summary>#rrggbb or #rrggbbaa</summary>
        public string Color { get; set; } = "#ffffff33";
    }

    /// <summary>Battle map image in world pixels; the DM aligns it to the grid.</summary>
    public class MapLayer
    {
        public string? ImageUrl { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public class FogState
    {
        public bool Enabled { get; set; }
        /// <summary>Applied in order over full fog: reveal cuts holes, hide paints fog back.</summary>
        public List<FogOp> Ops { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FogShape
    {
        Rect,
        Brush
    }

    public class FogOp
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public bool Reveal { get; set; }
        public FogShape Shape { get; set; }
        /// <summary>Rect: x1, y1, x2, y2. Brush: polyline x0, y0, x1, y1, ...</summary>
        public List<double> Points { get; set; } = new();
        /// <summary>Brush radius in world pixels.</summary>
        public double Radius { get; set; } = 40;
    }

    /// <summary>Token first so documents from before layers load onto the token layer.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TableLayer
    {
        Token,
        Map,
        Dm
    }

    /// <summary>Animated look for a template; null keeps it a plain rules template.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FxKind
    {
        Beam,
        Ray,
        Lightning,
        Breath,
        Fire,
        Bubble,
        Frost,
        Cloud,
        Darkness,
        Light,
        Arcane,
        Explosion,
        Healing,
        Vortex,
        Acid,
        Thunder,
        Necrotic,
        Web,
        Entangle,
        Wind
    }

    public class TableToken
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Name { get; set; } = "Token";

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CharacterId { get; set; }

        /// <summary>User ids allowed to move this token (copied from the character).</summary>
        public List<string> OwnerIds { get; set; } = new();

        public string? ImageUrl { get; set; }
        public string Color { get; set; } = "#f87171";

        /// <summary>World pixel position; clients render it snapped to the containing cell.</summary>
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>Diameter in cells.</summary>
        public double Size { get; set; } = 1;
        /// <summary>DM-layer tokens never reach players.</summary>
        public TableLayer Layer { get; set; }

        /// <summary>Pre-layers "Hidden" flag: old documents still load, as DM-layer tokens. Never written back.</summary>
        [BsonElement("Hidden"), BsonIgnoreIfDefault, JsonIgnore]
        public bool LegacyHidden
        {
            get => false;
            set { if (value) Layer = TableLayer.Dm; }
        }

        /// <summary>Healthy / Wounded / Bloodied / Down, filled per snapshot for players who don't get the numbers.</summary>
        [BsonIgnore]
        public string? Health { get; set; }

        public TableToken ShallowCopy() => (TableToken)MemberwiseClone();

        /// <summary>Non-null puts the token into the initiative order.</summary>
        public int? Initiative { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int TempHp { get; set; }
        public int Ac { get; set; }
        public int Speed { get; set; } = 30;

        /// <summary>Aura reach in ft beyond the token's edge; 0 = no aura. Drawn under tokens and moves with the token.</summary>
        public int AuraFt { get; set; }
        public string AuraColor { get; set; } = "#facc15";

        public List<TableEffect> Effects { get; set; } = new();
        public TurnEconomy Economy { get; set; } = new();
    }

    public class TableEffect
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Label { get; set; } = string.Empty;
        /// <summary>Turns of the affected token left; null = indefinite.</summary>
        public int? Remaining { get; set; }
    }

    public class TurnEconomy
    {
        public bool Action { get; set; }
        public bool Bonus { get; set; }
        public bool Reaction { get; set; }
        public int MovedFt { get; set; }
    }

    public class TableTurn
    {
        public bool Active { get; set; }
        public int Round { get; set; }
        public string? CurrentTokenId { get; set; }
    }

    public class TableStroke
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string UserId { get; set; } = string.Empty;
        public TableLayer Layer { get; set; }
        public string Color { get; set; } = "#ffffff";
        public double Width { get; set; } = 3;
        /// <summary>Flat list: x0, y0, x1, y1, ...</summary>
        public List<double> Points { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AoeKind
    {
        Circle,
        Cone,
        Line,
        Cube
    }

    public class AoeTemplate
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string UserId { get; set; } = string.Empty;
        public TableLayer Layer { get; set; }
        public AoeKind Kind { get; set; }
        public FxKind? Fx { get; set; }
        /// <summary>Origin in world pixels.</summary>
        public double X { get; set; }
        public double Y { get; set; }
        public int SizeFt { get; set; } = 20;
        /// <summary>Line width in ft.</summary>
        public int WidthFt { get; set; } = 5;
        /// <summary>Cube centred on its origin (grid-aligned) instead of extending from it.</summary>
        public bool Centered { get; set; }
        /// <summary>Token the template is pinned to; it moves with that token.</summary>
        public string? TokenId { get; set; }
        /// <summary>Lines pinned to a token can aim at a second one: the line runs between them as either moves.</summary>
        public string? TargetTokenId { get; set; }
        /// <summary>Direction in degrees (cone/line/cube).</summary>
        public double Angle { get; set; }
        public string Color { get; set; } = "#f97316";
        /// <summary>Rounds left; null = until removed.</summary>
        public int? Remaining { get; set; }
    }

    public class TableLogEntry
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        /// <summary>Who the roll is for (character name), defaults to the user name.</summary>
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<DiceRollResult> Rolls { get; set; } = new();
        public bool Private { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
    }

    // ── Hub contracts ──

    public record TableRollRequest(string? Label, List<string> Expressions, string? As, bool Private);

    public record TableSnapshot(
        GridSettings Grid,
        MapLayer Map,
        List<TableToken> Tokens,
        TableTurn Turn,
        List<AoeTemplate> Templates,
        TableEncounterInfo? Encounter);

    public class TableEncounterInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>A whole board, saved on an Encounter so it can be loaded back onto the table.</summary>
    public class StoredBoard
    {
        public GridSettings Grid { get; set; } = new();
        public MapLayer Map { get; set; } = new();
        public FogState Fog { get; set; } = new();
        public List<TableToken> Tokens { get; set; } = new();
        public List<AoeTemplate> Templates { get; set; } = new();
        public List<TableStroke> Strokes { get; set; } = new();
    }

    public record TableEncounterSummary(
        string Id,
        string Name,
        EncounterStatus Status,
        DateTime? StartedAt,
        DateTime? EndedAt,
        int TokenCount,
        bool HasBoard,
        bool Active);

    /// <summary>Everything a client needs after the board was swapped wholesale (encounter start/end/load).</summary>
    public record TableResync(TableSnapshot State, List<TableStroke> Strokes, FogState Fog, List<TableLogEntry> Log);

    public record TableJoinResult(
        string TableId,
        string CampaignId,
        string CampaignName,
        string JoinCode,
        bool IsDm,
        TableSnapshot State,
        List<TableStroke> Strokes,
        FogState Fog,
        List<TableLogEntry> Log);

    /// <summary>Who is calling and which table their connection joined.</summary>
    public record TableCaller(string UserId, string Name, bool IsAdmin);

    public record TableContext(string TableId, string UserId, string Name, bool IsDm);

    /// <summary>Someone connected to a table right now.</summary>
    public record TableParticipant(string UserId, string Name, bool IsDm);
}
