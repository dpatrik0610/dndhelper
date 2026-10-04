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

        public TabletopHub(ITabletopService service)
        {
            _service = service;
        }

        private TableCaller Caller => new(
            Context.UserIdentifier ?? throw new HubException("Not signed in."),
            Context.User?.Identity?.Name ?? "Player",
            Context.User?.IsInRole("Admin") == true);

        private TableContext Table =>
            Context.Items.TryGetValue(ContextKey, out var value) && value is TableContext ctx
                ? ctx
                : throw new HubException("Join a table first.");

        public Task<string> OpenForCampaign(string campaignId) => _service.OpenForCampaignAsync(Caller, campaignId);

        public async Task<TableJoinResult> Join(string code)
        {
            var caller = Caller;
            var result = await _service.JoinAsync(caller, code);

            if (Context.Items.TryGetValue(ContextKey, out var previous) && previous is TableContext old)
                await LeaveGroups(old);

            await Groups.AddToGroupAsync(Context.ConnectionId, TabletopGroups.All(result.TableId));
            await Groups.AddToGroupAsync(Context.ConnectionId,
                result.IsDm ? TabletopGroups.Dm(result.TableId) : TabletopGroups.Players(result.TableId));

            Context.Items[ContextKey] = new TableContext(result.TableId, caller.UserId, caller.Name, result.IsDm);
            return result;
        }

        public async Task Leave()
        {
            if (Context.Items.TryGetValue(ContextKey, out var value) && value is TableContext ctx)
            {
                await ClearMeasure(ctx);
                await LeaveGroups(ctx);
                Context.Items.Remove(ContextKey);
            }
        }

        public Task MoveToken(string tokenId, double x, double y, int distanceFt) =>
            _service.MoveTokenAsync(Table, tokenId, x, y, distanceFt);
        public Task UpsertToken(TableToken token) => _service.UpsertTokenAsync(Table, token);
        public Task RemoveToken(string tokenId) => _service.RemoveTokenAsync(Table, tokenId);
        public Task SetEconomy(string tokenId, TurnEconomy economy) => _service.SetEconomyAsync(Table, tokenId, economy);

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
            if (Context.Items.TryGetValue(ContextKey, out var value) && value is TableContext ctx)
                await ClearMeasure(ctx);

            await base.OnDisconnectedAsync(exception);
        }

        private Task ClearMeasure(TableContext ctx) =>
            Clients.OthersInGroup(TabletopGroups.All(ctx.TableId))
                .SendAsync("Measure", new { userId = ctx.UserId, name = ctx.Name, points = (List<double>?)null });

        private async Task LeaveGroups(TableContext ctx)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TabletopGroups.All(ctx.TableId));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TabletopGroups.Dm(ctx.TableId));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TabletopGroups.Players(ctx.TableId));
        }
    }
}
