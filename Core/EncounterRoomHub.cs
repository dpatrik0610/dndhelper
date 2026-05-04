using dndhelper.Models.EncounterRoomModels;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Threading.Tasks;

namespace dndhelper.Core
{
    [Authorize]
    public class EncounterRoomHub : Hub
    {
        private readonly IEncounterRoomService _roomService;
        private readonly ILogger _logger;

        public EncounterRoomHub(IEncounterRoomService roomService, ILogger logger)
        {
            _roomService = roomService;
            _logger = logger;
        }

        private async Task ExecuteRoomAction<T>(
            string methodName,
            RoomActionEnvelope<T> envelope,
            Func<Task> action)
        {
            _logger.Information(
                "EncounterRoomHub {MethodName} received. ConnectionId={ConnectionId}, RoomId={RoomId}, ExpectedRevision={ExpectedRevision}, Action={@Action}",
                methodName,
                Context.ConnectionId,
                envelope.RoomId,
                envelope.ExpectedRevision,
                envelope.Action);

            try
            {
                await action();

                _logger.Information(
                    "EncounterRoomHub {MethodName} completed. ConnectionId={ConnectionId}, RoomId={RoomId}",
                    methodName,
                    Context.ConnectionId,
                    envelope.RoomId);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "EncounterRoomHub {MethodName} failed. ConnectionId={ConnectionId}, RoomId={RoomId}, ExpectedRevision={ExpectedRevision}, Action={@Action}",
                    methodName,
                    Context.ConnectionId,
                    envelope.RoomId,
                    envelope.ExpectedRevision,
                    envelope.Action);
                throw;
            }
        }

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var userId = httpContext?.Request.Query["userId"].ToString();

            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.Information(
                    "EncounterRoomHub connected. UserId={UserId}, ConnectionId={ConnectionId}",
                    userId,
                    Context.ConnectionId);
            }
            else
            {
                _logger.Warning(
                    "EncounterRoomHub connected without userId. ConnectionId={ConnectionId}",
                    Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var httpContext = Context.GetHttpContext();
            var userId = httpContext?.Request.Query["userId"].ToString();

            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
            }

            _logger.Information(
                exception,
                "EncounterRoomHub disconnected. UserId={UserId}, ConnectionId={ConnectionId}",
                userId,
                Context.ConnectionId);

            await base.OnDisconnectedAsync(exception);
        }

        public async Task<object> JoinRoom(string joinCode)
        {
            _logger.Information(
                "EncounterRoomHub JoinRoom received. ConnectionId={ConnectionId}, JoinCode={JoinCode}",
                Context.ConnectionId,
                joinCode);

            try
            {
                var response = await _roomService.JoinRoomAsync(joinCode);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{response.RoomId}");

                _logger.Information(
                    "EncounterRoomHub JoinRoom completed. ConnectionId={ConnectionId}, RoomId={RoomId}",
                    Context.ConnectionId,
                    response.RoomId);

                await Clients.Caller.SendAsync("RoomStateSync", response.RoomState);
                return new { roomId = response.RoomId };
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "EncounterRoomHub JoinRoom failed. ConnectionId={ConnectionId}, JoinCode={JoinCode}",
                    Context.ConnectionId,
                    joinCode);
                throw;
            }
        }

        public async Task LeaveRoom(string roomId)
        {
            _logger.Information(
                "EncounterRoomHub LeaveRoom received. ConnectionId={ConnectionId}, RoomId={RoomId}",
                Context.ConnectionId,
                roomId);

            try
            {
                await _roomService.LeaveRoomAsync(roomId);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room_{roomId}");

                _logger.Information(
                    "EncounterRoomHub LeaveRoom completed. ConnectionId={ConnectionId}, RoomId={RoomId}",
                    Context.ConnectionId,
                    roomId);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "EncounterRoomHub LeaveRoom failed. ConnectionId={ConnectionId}, RoomId={RoomId}",
                    Context.ConnectionId,
                    roomId);
                throw;
            }
        }

        public async Task ReJoinRoom(string roomId)
        {
            _logger.Information(
                "EncounterRoomHub ReJoinRoom received. ConnectionId={ConnectionId}, RoomId={RoomId}",
                Context.ConnectionId,
                roomId);

            try
            {
                var room = await _roomService.GetRoomAsync(roomId);
                if (room == null)
                {
                    _logger.Warning(
                        "EncounterRoomHub ReJoinRoom found no room. ConnectionId={ConnectionId}, RoomId={RoomId}",
                        Context.ConnectionId,
                        roomId);
                    return;
                }

                await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{roomId}");
                await Clients.Caller.SendAsync("RoomStateSync", room);

                _logger.Information(
                    "EncounterRoomHub ReJoinRoom completed. ConnectionId={ConnectionId}, RoomId={RoomId}",
                    Context.ConnectionId,
                    roomId);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "EncounterRoomHub ReJoinRoom failed. ConnectionId={ConnectionId}, RoomId={RoomId}",
                    Context.ConnectionId,
                    roomId);
                throw;
            }
        }

        public Task AddEntity(RoomActionEnvelope<AddEntityRequest> envelope) =>
            ExecuteRoomAction(nameof(AddEntity), envelope, () =>
                _roomService.AddEntityAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action));

        public Task UpdateEntity(RoomActionEnvelope<UpdateEntityRequest> envelope) =>
            ExecuteRoomAction(nameof(UpdateEntity), envelope, () =>
                _roomService.UpdateEntityAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action));

        public Task RemoveEntity(RoomActionEnvelope<RemoveEntityRequest> envelope) =>
            ExecuteRoomAction(nameof(RemoveEntity), envelope, () =>
                _roomService.RemoveEntityAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.EntityId));

        public Task AddToken(RoomActionEnvelope<AddTokenRequest> envelope) =>
            ExecuteRoomAction(nameof(AddToken), envelope, () =>
                _roomService.AddTokenAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action));

        public Task MoveToken(RoomActionEnvelope<MoveTokenRequest> envelope) =>
            ExecuteRoomAction(nameof(MoveToken), envelope, () =>
                _roomService.MoveTokenAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action));

        public Task RemoveToken(RoomActionEnvelope<RemoveTokenRequest> envelope) =>
            ExecuteRoomAction(nameof(RemoveToken), envelope, () =>
                _roomService.RemoveTokenAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.TokenId));

        public Task AddMapElement(RoomActionEnvelope<AddMapElementRequest> envelope) =>
            ExecuteRoomAction(nameof(AddMapElement), envelope, () =>
                _roomService.AddMapElementAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action));

        public Task RemoveMapElement(RoomActionEnvelope<RemoveMapElementRequest> envelope) =>
            ExecuteRoomAction(nameof(RemoveMapElement), envelope, () =>
                _roomService.RemoveMapElementAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.ElementId));

        public Task ClearMapElements(RoomActionEnvelope<object> envelope) =>
            ExecuteRoomAction(nameof(ClearMapElements), envelope, () =>
                _roomService.ClearMapElementsAsync(envelope.RoomId, envelope.ExpectedRevision));

        public Task SetInitiative(RoomActionEnvelope<SetInitiativeRequest> envelope) =>
            ExecuteRoomAction(nameof(SetInitiative), envelope, () =>
                _roomService.SetInitiativeAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action));

        public Task AdvanceTurn(RoomActionEnvelope<object> envelope) =>
            ExecuteRoomAction(nameof(AdvanceTurn), envelope, () =>
                _roomService.AdvanceTurnAsync(envelope.RoomId, envelope.ExpectedRevision));

        public Task StartCombat(RoomActionEnvelope<object> envelope) =>
            ExecuteRoomAction(nameof(StartCombat), envelope, () =>
                _roomService.StartCombatAsync(envelope.RoomId, envelope.ExpectedRevision));

        public Task EndCombat(RoomActionEnvelope<object> envelope) =>
            ExecuteRoomAction(nameof(EndCombat), envelope, () =>
                _roomService.EndCombatAsync(envelope.RoomId, envelope.ExpectedRevision));

        public Task UpdateMapSettings(RoomActionEnvelope<UpdateMapSettingsRequest> envelope) =>
            ExecuteRoomAction(nameof(UpdateMapSettings), envelope, () =>
                _roomService.UpdateMapSettingsAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action));

        public Task AddInventory(RoomActionEnvelope<AddInventoryRequest> envelope) =>
            ExecuteRoomAction(nameof(AddInventory), envelope, () =>
                _roomService.AddInventoryAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.InventoryId));

        public Task RemoveInventory(RoomActionEnvelope<RemoveInventoryRequest> envelope) =>
            ExecuteRoomAction(nameof(RemoveInventory), envelope, () =>
                _roomService.RemoveInventoryAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.InventoryId));
    }
}
