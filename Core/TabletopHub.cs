using dndhelper.Models;
using dndhelper.Services;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Core
{
    /// <summary>
    /// Thin real-time entry point for the tabletop. A connection joins one table via its code;
    /// the table id and DM flag are kept on the connection so later calls can't target another table.
    /// </summary>
    [Authorize]
    public class TabletopHub : Hub
    {
        private const string ContextKey = "tabletop";
        private readonly ITabletopService _service;
        private readonly TablePresence _presence;
        private readonly IChatService _chat;

        public TabletopHub(ITabletopService service, TablePresence presence, IChatService chat)
        {
            _service = service;
            _presence = presence;
            _chat = chat;
        }

        private TableCaller Caller => new(
            Context.UserIdentifier ?? throw new HubException("Not signed in."),
            Context.User?.Identity?.Name ?? "Player",
            Context.User?.IsInRole("Admin") == true);

        // A kicked connection keeps its context but loses its seat, so it can't act on the table any more.
        private TableContext Table =>
            Context.Items.TryGetValue(ContextKey, out var value) && value is TableContext ctx && _presence.Contains(Context.ConnectionId, ctx.TableId)
                ? ctx
                : throw new HubException("Join a table first.");

        public Task<string> OpenForCampaign(string campaignId) => _service.OpenForCampaignAsync(Caller, campaignId);

        public async Task<TableJoinResult> Join(string code)
        {
            var caller = Caller;
            var result = await _service.JoinAsync(caller, code);

            if (Context.Items.TryGetValue(ContextKey, out var previous) && previous is TableContext old)
                await Unseat(old);

            await Groups.AddToGroupAsync(Context.ConnectionId, TabletopGroups.All(result.TableId));
            await Groups.AddToGroupAsync(Context.ConnectionId,
                result.IsDm ? TabletopGroups.Dm(result.TableId) : TabletopGroups.Players(result.TableId));

            var ctx = new TableContext(result.TableId, result.CampaignId, caller.UserId, caller.Name, result.IsDm);
            Context.Items[ContextKey] = ctx;
            _presence.Add(Context.ConnectionId, ctx);
            await SendParticipants(result.TableId);
            return result with { Chat = await _chat.PageAsync(Member(ctx), result.CampaignId, null) };
        }

        public async Task Leave()
        {
            if (Context.Items.TryGetValue(ContextKey, out var value) && value is TableContext ctx)
            {
                await Unseat(ctx);
                Context.Items.Remove(ContextKey);
            }
        }

        /// <summary>
        /// Removes a player from the table: all their tabs drop out and are told why.
        /// They can come back with the room code, so a new code keeps them out.
        /// </summary>
        public async Task Kick(string userId)
        {
            var ctx = RequireDm();
            if (await KickWhere(ctx.TableId, s => s.UserId == userId && !s.IsDm) == 0)
                throw new HubException("They're not at the table (DMs can't be kicked).");
        }

        /// <summary>Kicks every player; DMs stay. Returns how many people left.</summary>
        public Task<int> KickAll() => KickWhere(RequireDm().TableId, s => !s.IsDm);

        public Task MoveToken(string tokenId, double x, double y, int distanceFt) =>
            _service.MoveTokenAsync(Table, tokenId, x, y, distanceFt);
        public Task UpsertToken(TableToken token) => _service.UpsertTokenAsync(Table, token);
        public Task RemoveToken(string tokenId) => _service.RemoveTokenAsync(Table, tokenId);
        public Task SetEconomy(string tokenId, TurnEconomy economy) => _service.SetEconomyAsync(Table, tokenId, economy);
        /// <summary>Null value = roll d20 + initiative bonus on the server.</summary>
        public Task<TableLogEntry?> SetInitiative(string tokenId, int? value) => _service.SetInitiativeAsync(Table, tokenId, value);

        public Task UpdateGrid(GridSettings grid) => _service.UpdateGridAsync(Table, grid);
        public Task UpdateMap(MapLayer map) => _service.UpdateMapAsync(Table, map);

        public Task StartCombat() => _service.StartCombatAsync(Table);
        public Task EndCombat() => _service.EndCombatAsync(Table);
        public Task SetTurn(string tokenId) => _service.SetTurnAsync(Table, tokenId);
        public Task EndTurn() => _service.EndTurnAsync(Table);

        public Task AddStroke(TableStroke stroke) => _service.AddStrokeAsync(Table, stroke);
        public Task RemoveStroke(string strokeId) => _service.RemoveStrokeAsync(Table, strokeId);
        public Task ClearStrokes() => _service.ClearStrokesAsync(Table);

        public Task AddTemplate(AoeTemplate template) => _service.AddTemplateAsync(Table, template);
        public Task RemoveTemplate(string templateId) => _service.RemoveTemplateAsync(Table, templateId);
        public Task ClearTemplates() => _service.ClearTemplatesAsync(Table);

        public Task SetFogEnabled(bool enabled) => _service.SetFogEnabledAsync(Table, enabled);
        public Task AddFogOp(FogOp op) => _service.AddFogOpAsync(Table, op);
        public Task UndoFog() => _service.UndoFogAsync(Table);
        public Task ResetFog(bool revealAll) => _service.ResetFogAsync(Table, revealAll);

        /// <summary>Returns the entry so the roller can animate the real dice.</summary>
        public Task<TableLogEntry> Roll(TableRollRequest request) => _service.RollAsync(Table, request);
        // ── Campaign chat: everyone at the table, whispers only to who may read them ──

        public async Task SendChat(ChatSendRequest request)
        {
            var ctx = Table;
            var message = await Chat(() => _chat.SendAsync(Member(ctx), ctx.CampaignId, request));
            await ToAudience(ctx, message).SendAsync("ChatAdded", message);
        }

        public async Task EditChat(string messageId, string text)
        {
            var ctx = Table;
            var message = await Chat(() => _chat.EditAsync(Member(ctx), ctx.CampaignId, messageId, text));
            await ToAudience(ctx, message).SendAsync("ChatUpdated", message);
        }

        public async Task DeleteChat(string messageId)
        {
            var ctx = Table;
            var message = await Chat(() => _chat.DeleteAsync(Member(ctx), ctx.CampaignId, messageId));
            await ToAudience(ctx, message).SendAsync("ChatRemoved", message.Id);
        }

        /// <summary>The page of messages before the given one.</summary>
        public Task<ChatPage> ChatHistory(string beforeId)
        {
            var ctx = Table;
            return Chat(() => _chat.PageAsync(Member(ctx), ctx.CampaignId, beforeId));
        }

        public Task<int> InvitePlayers() => _service.InvitePlayersAsync(Table);

        public Task<List<TableEncounterSummary>> ListEncounters() => _service.ListEncountersAsync(Table);
        public Task StartEncounter(string? name) => _service.StartEncounterAsync(Table, name);
        public Task EndEncounter() => _service.EndEncounterAsync(Table);
        public Task LoadEncounter(string encounterId) => _service.LoadEncounterAsync(Table, encounterId);
        public Task DeleteEncounter(string encounterId) => _service.DeleteEncounterAsync(Table, encounterId);
        public Task<string> RegenerateCode() => _service.RegenerateCodeAsync(Table);

        /// <summary>Live ruler, relayed to everyone else and never stored. Null clears it.</summary>
        public Task Measure(List<double>? points)
        {
            var ctx = Table;
            if (points != null && (points.Count != 4 || points.Any(p => !double.IsFinite(p))))
                throw new HubException("Invalid measurement.");

            return Clients.OthersInGroup(TabletopGroups.All(ctx.TableId))
                .SendAsync("Measure", new { userId = ctx.UserId, name = ctx.Name, points });
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Groups clean themselves up on disconnect; the seat and any live ruler don't.
            if (Context.Items.TryGetValue(ContextKey, out var value) && value is TableContext ctx && _presence.Contains(Context.ConnectionId, ctx.TableId))
            {
                _presence.Remove(Context.ConnectionId, ctx.TableId);
                await ClearMeasure(ctx);
                await SendParticipants(ctx.TableId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private static ChatMember Member(TableContext ctx) => new(ctx.UserId, ctx.Name, ctx.IsDm);

        private IClientProxy ToAudience(TableContext ctx, ChatMessage message) =>
            message.Whisper
                ? Clients.Clients(_presence.Connections(ctx.TableId, s => message.VisibleTo(Member(s))))
                : Clients.Group(TabletopGroups.All(ctx.TableId));

        /// <summary>Chat rule violations reach the caller as their message.</summary>
        private static async Task<T> Chat<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (ArgumentException ex)
            {
                throw new HubException(ex.Message);
            }
        }

        private TableContext RequireDm()
        {
            var ctx = Table;
            if (!ctx.IsDm) throw new HubException("Only the DM can do that.");
            return ctx;
        }

        private async Task<int> KickWhere(string tableId, Func<TableContext, bool> match)
        {
            var removed = _presence.RemoveWhere(tableId, match);
            if (removed.Count == 0) return 0;

            foreach (var (connectionId, _) in removed)
                await LeaveGroups(connectionId, tableId);
            await Clients.Clients(removed.Select(r => r.ConnectionId).ToList()).SendAsync("Kicked");
            foreach (var seat in removed.Select(r => r.Seat).DistinctBy(s => s.UserId))
                await ClearMeasure(seat);
            await SendParticipants(tableId);
            return removed.Select(r => r.Seat.UserId).Distinct().Count();
        }

        private async Task Unseat(TableContext ctx)
        {
            _presence.Remove(Context.ConnectionId, ctx.TableId);
            await ClearMeasure(ctx);
            await LeaveGroups(Context.ConnectionId, ctx.TableId);
            await SendParticipants(ctx.TableId);
        }

        /// <summary>Only the DMs see who is connected.</summary>
        private Task SendParticipants(string tableId) =>
            Clients.Group(TabletopGroups.Dm(tableId)).SendAsync("Participants", _presence.Participants(tableId));

        private Task ClearMeasure(TableContext ctx) =>
            Clients.Group(TabletopGroups.All(ctx.TableId))
                .SendAsync("Measure", new { userId = ctx.UserId, name = ctx.Name, points = (List<double>?)null });

        private async Task LeaveGroups(string connectionId, string tableId)
        {
            await Groups.RemoveFromGroupAsync(connectionId, TabletopGroups.All(tableId));
            await Groups.RemoveFromGroupAsync(connectionId, TabletopGroups.Dm(tableId));
            await Groups.RemoveFromGroupAsync(connectionId, TabletopGroups.Players(tableId));
        }
    }
}
