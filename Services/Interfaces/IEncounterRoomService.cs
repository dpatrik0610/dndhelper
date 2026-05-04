using dndhelper.Models;
using dndhelper.Models.EncounterRoomModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IEncounterRoomService
    {
        // ── Room lifecycle ──
        Task<EncounterRoom> CreateRoomAsync(string name, MapSettings? mapSettings);
        Task<string> RegenerateJoinCodeAsync(string roomId);
        Task<JoinRoomResponse> JoinRoomAsync(string joinCode);
        Task LeaveRoomAsync(string roomId);
        Task<EncounterRoom?> GetRoomAsync(string roomId);
        Task<List<EncounterRoom>> GetMyRoomsAsync();
        Task EndRoomAsync(string roomId);

        // ── Entity management ──
        Task<int> AddEntityAsync(string roomId, int expectedRevision, AddEntityRequest request);
        Task<int> UpdateEntityAsync(string roomId, int expectedRevision, UpdateEntityRequest request);
        Task<int> RemoveEntityAsync(string roomId, int expectedRevision, string entityId);

        // ── Token management ──
        Task<int> AddTokenAsync(string roomId, int expectedRevision, AddTokenRequest request);
        Task<int> MoveTokenAsync(string roomId, int expectedRevision, MoveTokenRequest request);
        Task<int> RemoveTokenAsync(string roomId, int expectedRevision, string tokenId);

        // ── Map drawing ──
        Task<int> AddMapElementAsync(string roomId, int expectedRevision, AddMapElementRequest request);
        Task<int> RemoveMapElementAsync(string roomId, int expectedRevision, string elementId);
        Task<int> ClearMapElementsAsync(string roomId, int expectedRevision);

        // ── Turn management ──
        Task<int> SetInitiativeAsync(string roomId, int expectedRevision, SetInitiativeRequest request);
        Task<int> AdvanceTurnAsync(string roomId, int expectedRevision);
        Task<int> StartCombatAsync(string roomId, int expectedRevision);
        Task<int> EndCombatAsync(string roomId, int expectedRevision);

        // ── Map settings ──
        Task<int> UpdateMapSettingsAsync(string roomId, int expectedRevision, UpdateMapSettingsRequest request);

        // ── Inventory ──
        Task<int> AddInventoryAsync(string roomId, int expectedRevision, string inventoryId);
        Task<int> RemoveInventoryAsync(string roomId, int expectedRevision, string inventoryId);

        // ── Invites ──
        Task InvitePlayersAsync(string roomId, List<string> userIds);
    }
}
