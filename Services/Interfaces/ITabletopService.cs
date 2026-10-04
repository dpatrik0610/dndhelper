using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ITabletopService
    {
        Task<string> OpenForCampaignAsync(TableCaller caller, string campaignId);
        Task<TableJoinResult> JoinAsync(TableCaller caller, string joinCode);

        Task MoveTokenAsync(TableContext ctx, string tokenId, double x, double y, int distanceFt);
        Task UpsertTokenAsync(TableContext ctx, TableToken input);
        Task RemoveTokenAsync(TableContext ctx, string tokenId);
        Task SetEconomyAsync(TableContext ctx, string tokenId, TurnEconomy economy);

        Task UpdateGridAsync(TableContext ctx, GridSettings grid);
        Task UpdateMapAsync(TableContext ctx, MapLayer map);

        Task StartCombatAsync(TableContext ctx);
        Task EndCombatAsync(TableContext ctx);
        Task SetTurnAsync(TableContext ctx, string tokenId);
        Task EndTurnAsync(TableContext ctx);

        Task AddStrokeAsync(TableContext ctx, TableStroke input);
        Task RemoveStrokeAsync(TableContext ctx, string strokeId);
        Task ClearStrokesAsync(TableContext ctx);

        Task AddTemplateAsync(TableContext ctx, AoeTemplate input);
        Task RemoveTemplateAsync(TableContext ctx, string templateId);
        Task ClearTemplatesAsync(TableContext ctx);

        Task SetFogEnabledAsync(TableContext ctx, bool enabled);
        Task AddFogOpAsync(TableContext ctx, FogOp input);
        Task UndoFogAsync(TableContext ctx);
        Task ResetFogAsync(TableContext ctx, bool revealAll);

        Task<TableLogEntry> RollAsync(TableContext ctx, TableRollRequest request);

        Task<List<TableEncounterSummary>> ListEncountersAsync(TableContext ctx);
        Task StartEncounterAsync(TableContext ctx, string? name);
        Task EndEncounterAsync(TableContext ctx);
        Task LoadEncounterAsync(TableContext ctx, string encounterId);
        Task DeleteEncounterAsync(TableContext ctx, string encounterId);
        Task<int> InvitePlayersAsync(TableContext ctx);
        Task<string> RegenerateCodeAsync(TableContext ctx);

        /// <summary>Pushes character sheet changes (HP, AC, conditions...) onto its tokens.</summary>
        Task SyncCharacterAsync(Character character);

        Task<string> UploadImageAsync(string tableId, TableCaller caller, Stream content, string fileName);
        Task<(Stream Content, string ContentType)?> OpenImageAsync(string imageId);
    }
}
