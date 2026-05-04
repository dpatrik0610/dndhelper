using dndhelper.Models;
using dndhelper.Models.EncounterRoomModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    /// <summary>
    /// Action-based repository for EncounterRoom. Does NOT extend IRepository&lt;T&gt;
    /// because the room needs targeted atomic Mongo operations with revision guards,
    /// not generic CRUD.
    /// 
    /// All mutation methods return the new Revision on success, or throw ConcurrencyException.
    /// </summary>
    public interface IEncounterRoomRepository
    {
        // ── Lifecycle ──
        Task<EncounterRoom> CreateAsync(EncounterRoom room);
        Task<EncounterRoom?> GetByIdAsync(string roomId);
        Task<EncounterRoom?> GetByJoinCodeAsync(string joinCode);
        Task<List<EncounterRoom>> GetActiveRoomsByUserAsync(string userId);
        Task<bool> SoftDeleteAsync(string roomId);

        // ── Players ──
        Task<int> AddPlayerAsync(string roomId, int expectedRevision, string playerId);
        Task<int> RemovePlayerAsync(string roomId, int expectedRevision, string playerId);

        // ── Entities ──
        Task<int> AddEntityAsync(string roomId, int expectedRevision, SessionEntity entity);
        Task<int> UpdateEntityFieldsAsync(string roomId, int expectedRevision, string entityId, Dictionary<string, object> updates);
        Task<int> RemoveEntityAsync(string roomId, int expectedRevision, string entityId);

        // ── Tokens ──
        Task<int> AddTokenAsync(string roomId, int expectedRevision, RoomToken token);
        Task<int> MoveTokenAsync(string roomId, int expectedRevision, string tokenId, Point2D position);
        Task<int> RemoveTokenAsync(string roomId, int expectedRevision, string tokenId);

        // ── Map elements ──
        Task<int> AddMapElementAsync(string roomId, int expectedRevision, MapElement element);
        Task<int> RemoveMapElementAsync(string roomId, int expectedRevision, string elementId);
        Task<int> ClearMapElementsAsync(string roomId, int expectedRevision);

        // ── Turn state ──
        Task<int> UpdateTurnStateAsync(string roomId, int expectedRevision, TurnState turnState);

        // ── Map settings ──
        Task<int> UpdateMapSettingsAsync(string roomId, int expectedRevision, MapSettings mapSettings);

        // ── Inventory ──
        Task<int> AddInventoryAsync(string roomId, int expectedRevision, string inventoryId);
        Task<int> RemoveInventoryAsync(string roomId, int expectedRevision, string inventoryId);

        // ── Join code ──
        Task<string> RegenerateJoinCodeAsync(string roomId);
    }
}
