using System.Collections.Generic;

namespace dndhelper.Models.EncounterRoomModels
{
    // ── Room lifecycle ──

    public record CreateRoomRequest(
        string Name,
        MapSettings? MapSettings
    );

    public record JoinRoomRequest(string JoinCode);

    public record JoinRoomResponse(string RoomId, EncounterRoom RoomState);

    // ── Entity actions ──

    public record AddEntityRequest(
        string Name,
        bool IsPlayer,
        string? OwnerId,
        string? Color,
        Dictionary<string, object>? Attributes
    );

    public record UpdateEntityRequest(
        string EntityId,
        Dictionary<string, object> Updates
    );

    public record RemoveEntityRequest(string EntityId);

    // ── Token actions ──

    public record MoveTokenRequest(string TokenId, Point2D Position);

    public record AddTokenRequest(
        string EntityId,
        Point2D Position,
        int Size = 1,
        string? ImageUrl = null
    );

    public record RemoveTokenRequest(string TokenId);

    // ── Map drawing ──

    public record AddMapElementRequest(
        MapElementType Type,
        ShapeType? Shape,
        List<Point2D> Points,
        string Color,
        int Thickness
    );

    public record RemoveMapElementRequest(string ElementId);

    // ── Map settings ──

    public record UpdateMapSettingsRequest(
        string? MapImageUrl,
        GridType? GridType,
        double? GridCellSize,
        int? GridWidth,
        int? GridHeight
    );

    // ── Turn management ──

    public record SetInitiativeRequest(string EntityId, int Initiative);

    // ── Inventory linking ──

    public record AddInventoryRequest(string InventoryId);

    public record RemoveInventoryRequest(string InventoryId);

    // ── Invite ──

    public record InvitePlayersRequest(string RoomId, List<string> UserIds);

    // ── Concurrency envelope — every SignalR mutation wraps the action in this ──

    public record RoomActionEnvelope<T>(
        string RoomId,
        int ExpectedRevision,
        T Action
    );
}
