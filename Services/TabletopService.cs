using dndhelper.Core;
using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Models.RollModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public static class TabletopGroups
    {
        public static string All(string tableId) => $"tt:{tableId}";
        public static string Dm(string tableId) => $"tt:{tableId}:dm";
        public static string Players(string tableId) => $"tt:{tableId}:p";
    }

    /// <summary>
    /// All tabletop rules. Every write runs load → mutate → replace → broadcast under a per-table lock,
    /// so concurrent players never conflict and broadcasts go out in write order.
    /// </summary>
    public class TabletopService : ITabletopService
    {
        public const string ImagePathPrefix = "/api/tabletop/images/";

        private const int MaxTokens = 200;
        private const int MaxStrokes = 300;
        private const int MaxStrokeCoords = 1000;
        private const int MaxTemplates = 100;
        private const int MaxFogOps = 300;
        private const int MaxFogCoords = 1000;
        private const int MaxLog = 100;
        private const int MaxDicePerRoll = 100;
        private const double MaxCoord = 1_000_000;

        private static readonly Regex ColorRegex = new("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.Compiled);
        private static readonly Regex OwnImageRegex = new($"^{Regex.Escape(ImagePathPrefix)}([0-9a-f]{{24}})$", RegexOptions.Compiled);

        // ponytail: in-process per-table lock, fine for the single API instance (SignalR has no backplane either).
        // Scaling out needs a Redis backplane plus a version field on the document instead.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

        private readonly ITabletopRepository _repository;
        private readonly ICampaignRepository _campaigns;
        private readonly ICharacterRepository _characters;
        private readonly IEncounterRepository _encounters;
        private readonly IDiceRollService _dice;
        private readonly IEntitySyncService _entitySync;
        private readonly IHubContext<TabletopHub> _hub;
        private readonly ILogger _logger;

        public TabletopService(
            ITabletopRepository repository,
            ICampaignRepository campaigns,
            ICharacterRepository characters,
            IEncounterRepository encounters,
            IDiceRollService dice,
            IEntitySyncService entitySync,
            IHubContext<TabletopHub> hub,
            ILogger logger)
        {
            _repository = Guard.NotNull(repository, nameof(repository));
            _campaigns = Guard.NotNull(campaigns, nameof(campaigns));
            _characters = Guard.NotNull(characters, nameof(characters));
            _encounters = Guard.NotNull(encounters, nameof(encounters));
            _dice = Guard.NotNull(dice, nameof(dice));
            _entitySync = Guard.NotNull(entitySync, nameof(entitySync));
            _hub = Guard.NotNull(hub, nameof(hub));
            _logger = Guard.NotNull(logger, nameof(logger));
        }

        // ──────────────────────────────────────────────
        // Join
        // ──────────────────────────────────────────────

        public async Task<string> OpenForCampaignAsync(TableCaller caller, string campaignId)
        {
            if (!ObjectId.TryParse(campaignId, out _)) throw Fail("Pick a campaign.");
            var campaign = await _campaigns.GetByIdAsync(campaignId) ?? throw Fail("Campaign not found.");
            if (!IsDm(campaign, caller)) throw Fail("Only the campaign's DM can open its table.");

            var table = await _repository.GetOrCreateForCampaignAsync(campaignId);
            return table.JoinCode;
        }

        public async Task<TableJoinResult> JoinAsync(TableCaller caller, string joinCode)
        {
            var code = (joinCode ?? string.Empty).Trim().ToUpperInvariant();
            var table = await _repository.GetByJoinCodeAsync(code) ?? throw Fail("No table with that code.");
            var campaign = await _campaigns.GetByIdAsync(table.CampaignId);
            var isDm = campaign != null && IsDm(campaign, caller);

            return new TableJoinResult(
                table.Id!,
                table.CampaignId,
                campaign?.Name ?? "Campaign",
                table.JoinCode,
                isDm,
                Snapshot(table, isDm),
                isDm ? table.Strokes : table.Strokes.Where(s => s.Layer != TableLayer.Dm).ToList(),
                table.Fog,
                isDm ? table.Log : table.Log.Where(l => !l.Private).ToList());
        }

        // ──────────────────────────────────────────────
        // Tokens
        // ──────────────────────────────────────────────

        public Task MoveTokenAsync(TableContext ctx, string tokenId, double x, double y, int distanceFt) =>
            Change(ctx.TableId, t =>
            {
                var token = FindToken(t, tokenId);
                if (!ctx.IsDm)
                {
                    if (token.Layer != TableLayer.Token || !Owns(ctx, token)) throw Fail("You can only move your own token.");
                    if (t.Turn.Active && !IsTurnOf(t, token)) throw Fail("Wait for your turn to move.");
                }

                token.X = Coord(x);
                token.Y = Coord(y);
                foreach (var template in t.Templates.Where(tp => tp.TokenId == token.Id))
                {
                    template.X = token.X;
                    template.Y = token.Y;
                }
                if (IsTurnOf(t, token))
                    token.Economy.MovedFt = Math.Min(token.Economy.MovedFt + Math.Clamp(distanceFt, 0, 10_000), 100_000);
            });

        public Task UpsertTokenAsync(TableContext ctx, TableToken input)
        {
            RequireDm(ctx);
            Guard.NotNull(input, nameof(input));

            return ChangeAsync(ctx.TableId, async t =>
            {
                var existing = t.Tokens.FirstOrDefault(x => x.Id == input.Id);
                var token = existing ?? new TableToken();
                var before = existing == null ? null : CharacterFields(existing);

                token.Name = Text(input.Name, 60, "Token");
                token.ImageUrl = ImageUrl(input.ImageUrl);
                token.Color = Color(input.Color, "#f87171");
                token.X = Coord(input.X);
                token.Y = Coord(input.Y);
                token.Size = Math.Clamp(Finite(input.Size, 1), 0.5, 6);
                token.Layer = Enum.IsDefined(input.Layer) ? input.Layer : TableLayer.Token;
                token.Initiative = input.Initiative is int init ? Math.Clamp(init, -20, 99) : null;
                token.Hp = Math.Clamp(input.Hp, 0, 9999);
                token.MaxHp = Math.Clamp(input.MaxHp, 0, 9999);
                token.TempHp = Math.Clamp(input.TempHp, 0, 999);
                token.Ac = Math.Clamp(input.Ac, 0, 50);
                token.Speed = Math.Clamp(input.Speed, 0, 300);
                token.Effects = (input.Effects ?? new())
                    .Take(20)
                    .Select(e => new TableEffect
                    {
                        Id = string.IsNullOrWhiteSpace(e.Id) ? ObjectId.GenerateNewId().ToString() : Text(e.Id, 24, "x"),
                        Label = Text(e.Label, 40, "Effect"),
                        Remaining = e.Remaining is int r ? Math.Clamp(r, 1, 999) : null,
                    })
                    .ToList();
                var economy = input.Economy ?? new TurnEconomy();
                token.Economy = new TurnEconomy
                {
                    Action = economy.Action,
                    Bonus = economy.Bonus,
                    Reaction = economy.Reaction,
                    MovedFt = Math.Clamp(economy.MovedFt, 0, 100_000),
                };

                if (existing == null)
                {
                    if (t.Tokens.Count >= MaxTokens) throw Fail("Too many tokens on this table.");

                    if (!string.IsNullOrWhiteSpace(input.CharacterId))
                    {
                        var character = await _characters.GetByIdAsync(input.CharacterId)
                            ?? throw Fail("Character not found.");
                        if (character.CampaignId != t.CampaignId) throw Fail("That character is not in this campaign.");

                        token.CharacterId = character.Id;
                        ApplyCharacter(token, character);
                    }

                    t.Tokens.Add(token);
                }
                else if (token.CharacterId != null && before != CharacterFields(token))
                {
                    await WriteThroughAsync(ctx, t, token);
                }
            });
        }

        public Task RemoveTokenAsync(TableContext ctx, string tokenId)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t =>
            {
                var token = FindToken(t, tokenId);
                t.Tokens.Remove(token);
                // Pinned templates stay where the token was.
                foreach (var template in t.Templates.Where(tp => tp.TokenId == token.Id)) template.TokenId = null;
            });
        }

        /// <summary>
        /// Puts a token into the initiative order: a typed value, or (value null) d20 + the character's initiative
        /// bonus rolled here and logged for everyone. Players may only join once with their own token; the DM can always set it.
        /// </summary>
        public async Task<TableLogEntry?> SetInitiativeAsync(TableContext ctx, string tokenId, int? value)
        {
            TableLogEntry? entry = null;
            await ChangeAsync(ctx.TableId, async t =>
            {
                var token = FindToken(t, tokenId);
                if (!ctx.IsDm)
                {
                    if (token.Layer != TableLayer.Token || !Owns(ctx, token)) throw Fail("That's not your token.");
                    if (token.Initiative != null) throw Fail("You're already in the initiative order. Ask the DM to change it.");
                }

                if (value is int typed)
                {
                    token.Initiative = Math.Clamp(typed, -20, 99);
                    return;
                }

                var bonus = 0;
                if (token.CharacterId != null)
                    bonus = (await _characters.GetByIdAsync(token.CharacterId))?.Initiative ?? 0;
                var roll = _dice.RollDice(1, 20, Math.Clamp(bonus, -20, 30));
                token.Initiative = Math.Clamp(roll.Total, -20, 99);

                entry = new TableLogEntry
                {
                    UserId = ctx.UserId,
                    UserName = ctx.Name,
                    Name = token.Name,
                    Label = "Initiative",
                    Rolls = new List<DiceRollResult> { roll },
                };
                t.Log.Add(entry);
                if (t.Log.Count > MaxLog) t.Log.RemoveRange(0, t.Log.Count - MaxLog);
            }, async t =>
            {
                await BroadcastStateAsync(t);
                if (entry != null) await SendAll(t, "LogAdded", entry);
            });
            return entry;
        }

        public Task SetEconomyAsync(TableContext ctx, string tokenId, TurnEconomy economy) =>
            Change(ctx.TableId, t =>
            {
                var token = FindToken(t, tokenId);
                // Reactions happen off-turn, so owners may toggle any time.
                if (!ctx.IsDm && !Owns(ctx, token)) throw Fail("That's not your token.");

                token.Economy = new TurnEconomy
                {
                    Action = economy?.Action ?? false,
                    Bonus = economy?.Bonus ?? false,
                    Reaction = economy?.Reaction ?? false,
                    MovedFt = Math.Clamp(economy?.MovedFt ?? 0, 0, 100_000),
                };
            });

        // ──────────────────────────────────────────────
        // Grid & map
        // ──────────────────────────────────────────────

        public Task UpdateGridAsync(TableContext ctx, GridSettings grid)
        {
            RequireDm(ctx);
            if (grid == null || !Enum.IsDefined(grid.Type)) throw Fail("Unknown grid type.");

            var next = new GridSettings
            {
                Type = grid.Type,
                CellSize = Math.Clamp(grid.CellSize, 20, 200),
                Color = Color(grid.Color, "#ffffff33"),
            };
            return Change(ctx.TableId, t => t.Grid = next);
        }

        public Task UpdateMapAsync(TableContext ctx, MapLayer map)
        {
            RequireDm(ctx);
            Guard.NotNull(map, nameof(map));

            var next = new MapLayer
            {
                ImageUrl = ImageUrl(map.ImageUrl),
                X = Coord(map.X),
                Y = Coord(map.Y),
                Width = Math.Clamp(Finite(map.Width, 0), 0, 100_000),
                Height = Math.Clamp(Finite(map.Height, 0), 0, 100_000),
            };

            // ponytail: replaced uploads are kept because stored encounter boards may still show them.
            // Orphans pile up in GridFS; sweep images no table or encounter references if that ever matters.
            return Change(ctx.TableId, t => t.Map = next);
        }

        // ──────────────────────────────────────────────
        // Turn order
        // ──────────────────────────────────────────────

        public Task StartCombatAsync(TableContext ctx)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t =>
            {
                var order = InitiativeOrder(t);
                if (order.Count == 0) throw Fail("Give at least one token an initiative first.");

                t.Turn = new TableTurn { Active = true, Round = 1 };
                foreach (var token in t.Tokens) token.Economy = new TurnEconomy();
                BeginTurn(t, order[0], tickEffects: false);
            });
        }

        public Task EndCombatAsync(TableContext ctx)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t =>
            {
                t.Turn = new TableTurn();
                foreach (var token in t.Tokens) token.Economy = new TurnEconomy();
            });
        }

        public Task SetTurnAsync(TableContext ctx, string tokenId)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t =>
            {
                if (!t.Turn.Active) throw Fail("Start combat first.");
                var token = FindToken(t, tokenId);
                if (token.Initiative == null) throw Fail("That token has no initiative.");
                BeginTurn(t, token, tickEffects: false);
            });
        }

        public Task EndTurnAsync(TableContext ctx) =>
            ChangeAsync(ctx.TableId, async t =>
            {
                if (!t.Turn.Active) throw Fail("Combat is not running.");

                var order = InitiativeOrder(t);
                if (order.Count == 0) throw Fail("Nobody has initiative.");

                var index = order.FindIndex(x => x.Id == t.Turn.CurrentTokenId);
                if (!ctx.IsDm && (index < 0 || !Owns(ctx, order[index]))) throw Fail("It's not your turn.");

                var next = (index + 1) % order.Count;
                if (next <= index)
                {
                    t.Turn.Round++;
                    foreach (var template in t.Templates.Where(x => x.Remaining != null)) template.Remaining--;
                    t.Templates.RemoveAll(x => x.Remaining <= 0);
                }

                var token = order[next];
                var effectCount = token.Effects.Count;
                BeginTurn(t, token, tickEffects: true);

                if (token.CharacterId != null && token.Effects.Count != effectCount)
                    await WriteThroughAsync(ctx, t, token);
            });

        /// <summary>Initiative desc, then name — same order the panel shows.</summary>
        internal static List<TableToken> InitiativeOrder(Tabletop t) =>
            t.Tokens
                .Where(x => x.Initiative != null)
                .OrderByDescending(x => x.Initiative)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Effects tick on the token whose turn is starting, so an effect applied mid-turn lasts its full count.
        /// </summary>
        private static void BeginTurn(Tabletop t, TableToken token, bool tickEffects)
        {
            t.Turn.CurrentTokenId = token.Id;
            token.Economy = new TurnEconomy();
            if (!tickEffects) return;

            foreach (var effect in token.Effects.Where(e => e.Remaining != null)) effect.Remaining--;
            token.Effects.RemoveAll(e => e.Remaining <= 0);
        }

        // ──────────────────────────────────────────────
        // Drawings
        // ──────────────────────────────────────────────

        public Task AddStrokeAsync(TableContext ctx, TableStroke input)
        {
            Guard.NotNull(input, nameof(input));
            var stroke = new TableStroke
            {
                UserId = ctx.UserId,
                Layer = LayerFor(ctx, input.Layer),
                Color = Color(input.Color, "#ffffff"),
                Width = Math.Clamp(Finite(input.Width, 3), 1, 40),
                Points = Coords(input.Points, 4, MaxStrokeCoords),
            };

            return Change(ctx.TableId, t =>
            {
                if (t.Strokes.Count >= MaxStrokes) throw Fail("Too many drawings, clear some first.");
                t.Strokes.Add(stroke);
            }, t => stroke.Layer == TableLayer.Dm
                ? _hub.Clients.Group(TabletopGroups.Dm(t.Id!)).SendAsync("StrokeAdded", stroke)
                : SendAll(t, "StrokeAdded", stroke));
        }

        public Task RemoveStrokeAsync(TableContext ctx, string strokeId) =>
            Change(ctx.TableId, t =>
            {
                var stroke = t.Strokes.FirstOrDefault(s => s.Id == strokeId) ?? throw Fail("Drawing not found.");
                if (!ctx.IsDm && stroke.UserId != ctx.UserId) throw Fail("You can only erase your own drawings.");
                t.Strokes.Remove(stroke);
            }, t => SendAll(t, "StrokeRemoved", strokeId));

        public Task ClearStrokesAsync(TableContext ctx)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t => t.Strokes.Clear(), t => SendAll(t, "StrokesCleared"));
        }

        // ──────────────────────────────────────────────
        // AoE templates
        // ──────────────────────────────────────────────

        public Task AddTemplateAsync(TableContext ctx, AoeTemplate input)
        {
            Guard.NotNull(input, nameof(input));
            if (!Enum.IsDefined(input.Kind)) throw Fail("Unknown template shape.");

            var template = new AoeTemplate
            {
                UserId = ctx.UserId,
                Layer = LayerFor(ctx, input.Layer),
                Kind = input.Kind,
                Fx = input.Fx is FxKind fx && Enum.IsDefined(fx) ? fx : null,
                X = Coord(input.X),
                Y = Coord(input.Y),
                SizeFt = Math.Clamp(input.SizeFt, 5, 500),
                WidthFt = Math.Clamp(input.WidthFt, 5, 100),
                Centered = input.Kind == AoeKind.Cube && input.Centered,
                TokenId = string.IsNullOrWhiteSpace(input.TokenId) ? null : input.TokenId,
                Angle = Finite(input.Angle, 0) % 360,
                Color = Color(input.Color, "#f97316"),
                Remaining = input.Remaining is int r ? Math.Clamp(r, 1, 100) : null,
            };

            return Change(ctx.TableId, t =>
            {
                if (t.Templates.Count >= MaxTemplates) throw Fail("Too many templates, remove some first.");
                if (template.TokenId != null)
                {
                    var token = FindToken(t, template.TokenId);
                    if (token.Layer == TableLayer.Dm && !ctx.IsDm) throw Fail("Token not found.");
                    template.X = token.X;
                    template.Y = token.Y;
                }
                t.Templates.Add(template);
            });
        }

        public Task RemoveTemplateAsync(TableContext ctx, string templateId) =>
            Change(ctx.TableId, t =>
            {
                var template = t.Templates.FirstOrDefault(x => x.Id == templateId) ?? throw Fail("Template not found.");
                if (!ctx.IsDm && template.UserId != ctx.UserId) throw Fail("You can only remove your own templates.");
                t.Templates.Remove(template);
            });

        public Task ClearTemplatesAsync(TableContext ctx)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t => t.Templates.Clear());
        }

        // ──────────────────────────────────────────────
        // Fog of war
        // ──────────────────────────────────────────────

        public Task SetFogEnabledAsync(TableContext ctx, bool enabled)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t => t.Fog.Enabled = enabled, SendFog);
        }

        public Task AddFogOpAsync(TableContext ctx, FogOp input)
        {
            RequireDm(ctx);
            Guard.NotNull(input, nameof(input));
            if (!Enum.IsDefined(input.Shape)) throw Fail("Unknown fog shape.");

            var op = new FogOp
            {
                Reveal = input.Reveal,
                Shape = input.Shape,
                Points = input.Shape == FogShape.Rect
                    ? Coords(input.Points, 4, 4)
                    : Coords(input.Points, 2, MaxFogCoords),
                Radius = Math.Clamp(Finite(input.Radius, 40), 2, 2000),
            };

            return Change(ctx.TableId, t =>
            {
                if (t.Fog.Ops.Count >= MaxFogOps) throw Fail("The fog is too detailed. Use Hide all or Reveal all to start fresh.");
                t.Fog.Ops.Add(op);
            }, t => SendAll(t, "FogOpAdded", op));
        }

        public Task UndoFogAsync(TableContext ctx)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t =>
            {
                if (t.Fog.Ops.Count > 0) t.Fog.Ops.RemoveAt(t.Fog.Ops.Count - 1);
            }, SendFog);
        }

        public Task ResetFogAsync(TableContext ctx, bool revealAll)
        {
            RequireDm(ctx);
            return Change(ctx.TableId, t =>
            {
                t.Fog.Ops.Clear();
                if (revealAll)
                {
                    t.Fog.Ops.Add(new FogOp
                    {
                        Reveal = true,
                        Shape = FogShape.Rect,
                        Points = new List<double> { -MaxCoord, -MaxCoord, MaxCoord, MaxCoord },
                    });
                }
            }, SendFog);
        }

        // ──────────────────────────────────────────────
        // Rolls & room
        // ──────────────────────────────────────────────

        public async Task<TableLogEntry> RollAsync(TableContext ctx, TableRollRequest request)
        {
            var expressions = request?.Expressions?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? new();
            if (expressions.Count is 0 or > 4) throw Fail("Roll between 1 and 4 dice expressions.");

            var rolls = new List<DiceRollResult>();
            foreach (var expression in expressions)
            {
                try
                {
                    var (count, sides, modifier, _) = DiceExpressionParser.Parse(expression);
                    if (count > MaxDicePerRoll) throw Fail($"At most {MaxDicePerRoll} dice per roll.");
                    rolls.Add(_dice.RollDice(count, sides, modifier));
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    throw Fail($"Invalid dice expression '{Text(expression, 20, "")}'. Try something like 2d6+3.");
                }
            }

            var entry = new TableLogEntry
            {
                UserId = ctx.UserId,
                UserName = ctx.Name,
                Name = Text(request!.As, 40, ctx.Name),
                Label = Text(request.Label, 80, string.Empty),
                Rolls = rolls,
                Private = ctx.IsDm && request.Private,
            };

            await Change(ctx.TableId, t =>
            {
                t.Log.Add(entry);
                if (t.Log.Count > MaxLog) t.Log.RemoveRange(0, t.Log.Count - MaxLog);
            }, t => _hub.Clients
                .Group(entry.Private ? TabletopGroups.Dm(t.Id!) : TabletopGroups.All(t.Id!))
                .SendAsync("LogAdded", entry));
            return entry;
        }

        public async Task<int> InvitePlayersAsync(TableContext ctx)
        {
            RequireDm(ctx);
            var table = await _repository.GetByIdAsync(ctx.TableId) ?? throw Fail("This table no longer exists.");
            var campaign = await _campaigns.GetByIdAsync(table.CampaignId) ?? throw Fail("Campaign not found.");

            var characters = await _characters.GetByIdsAsync(campaign.CharacterIds ?? new List<string>());
            var dmIds = campaign.OwnerIds ?? new List<string>();
            var recipients = characters
                .Where(c => !c.IsDeleted)
                .SelectMany(c => c.OwnerIds ?? new List<string>())
                .Distinct()
                .Where(id => id != ctx.UserId && !dmIds.Contains(id))
                .ToList();

            if (recipients.Count > 0)
            {
                await _entitySync.BroadcastToUsers(
                    "TableInvite",
                    new { code = table.JoinCode, campaignName = campaign.Name, invitedBy = ctx.Name },
                    recipients);
            }

            _logger.Information("📨 {User} invited {Count} players to tabletop {TableId}", ctx.Name, recipients.Count, ctx.TableId);
            return recipients.Count;
        }

        public async Task<string> RegenerateCodeAsync(TableContext ctx)
        {
            RequireDm(ctx);
            var table = await Change(ctx.TableId,
                t => t.JoinCode = Tabletop.NewJoinCode(),
                t => _hub.Clients.Group(TabletopGroups.Dm(t.Id!)).SendAsync("CodeChanged", t.JoinCode));
            return table.JoinCode;
        }

        // ──────────────────────────────────────────────
        // Encounters: each one is a campaign Encounter with the board stored on it
        // ──────────────────────────────────────────────

        public async Task<List<TableEncounterSummary>> ListEncountersAsync(TableContext ctx)
        {
            RequireDm(ctx);
            var table = await _repository.GetByIdAsync(ctx.TableId) ?? throw Fail("This table no longer exists.");
            var encounters = await _encounters.GetByCampaignIdAsync(table.CampaignId);
            return encounters
                .Where(e => !e.IsDeleted && e.Id != null)
                .OrderByDescending(e => e.StartedAt ?? e.CreatedAt)
                .Select(e => new TableEncounterSummary(
                    e.Id!,
                    e.Name,
                    e.Status,
                    e.StartedAt,
                    e.EndedAt,
                    e.Board?.Tokens.Count ?? e.Entities.Count,
                    e.Board != null,
                    e.Id == table.Encounter?.Id))
                .ToList();
        }

        /// <summary>Stores the running encounter (if any), then starts a new one on a blank board (grid kept).</summary>
        public Task StartEncounterAsync(TableContext ctx, string? name)
        {
            RequireDm(ctx);
            var now = DateTime.UtcNow;
            var title = Text(name, 80, $"Encounter {now:MMM d, HH:mm}");

            return ChangeAsync(ctx.TableId, async t =>
            {
                await StoreActiveEncounterAsync(t);
                var campaign = await _campaigns.GetByIdAsync(t.CampaignId) ?? throw Fail("Campaign not found.");
                var encounter = await _encounters.CreateAsync(new Encounter
                {
                    CampaignId = t.CampaignId,
                    SessionId = ObjectId.TryParse(campaign.CurrentSessionId, out _) ? campaign.CurrentSessionId : null,
                    OwnerIds = campaign.OwnerIds?.ToList() ?? new List<string>(),
                    Name = title,
                    Status = EncounterStatus.Active,
                    StartedAt = now,
                });

                ApplyBoard(t, new StoredBoard { Grid = t.Grid });
                t.Log.Clear();
                t.Encounter = new TableEncounterInfo { Id = encounter.Id!, Name = title, StartedAt = now };
                await SetActiveEncounterAsync(t.CampaignId, encounter.Id);
            }, BroadcastResyncAsync);
        }

        /// <summary>Saves the board onto the running encounter and marks it completed. The board stays as it is.</summary>
        public Task EndEncounterAsync(TableContext ctx)
        {
            RequireDm(ctx);
            return ChangeAsync(ctx.TableId, async t =>
            {
                if (t.Encounter == null) throw Fail("No encounter is running.");
                await StoreActiveEncounterAsync(t);
            });
        }

        /// <summary>Stores the running encounter, then puts a stored one back on the table and makes it the active one.</summary>
        public Task LoadEncounterAsync(TableContext ctx, string encounterId)
        {
            RequireDm(ctx);
            return ChangeAsync(ctx.TableId, async t =>
            {
                if (t.Encounter?.Id == encounterId) throw Fail("That encounter is already on the table.");
                var encounter = await _encounters.GetByIdAsync(encounterId) ?? throw Fail("Encounter not found.");
                if (encounter.CampaignId != t.CampaignId) throw Fail("That encounter belongs to another campaign.");

                await StoreActiveEncounterAsync(t);
                ApplyBoard(t, encounter.Board ?? new StoredBoard { Grid = t.Grid });

                // Characters may have levelled, healed or changed since; tokens pick up the current sheets.
                var characterIds = t.Tokens.Where(x => x.CharacterId != null).Select(x => x.CharacterId!).Distinct().ToList();
                if (characterIds.Count > 0)
                {
                    var characters = await _characters.GetByIdsAsync(characterIds);
                    foreach (var token in t.Tokens.Where(x => x.CharacterId != null))
                    {
                        var character = characters.FirstOrDefault(c => c.Id == token.CharacterId);
                        if (character != null) ApplyCharacter(token, character);
                    }
                }

                var now = DateTime.UtcNow;
                encounter.Status = EncounterStatus.Active;
                encounter.StartedAt ??= now;
                encounter.EndedAt = null;
                encounter.UpdatedAt = now;
                await _encounters.UpdateAsync(encounter);

                t.Log.Clear();
                t.Encounter = new TableEncounterInfo { Id = encounter.Id!, Name = encounter.Name, StartedAt = encounter.StartedAt.Value };
                await SetActiveEncounterAsync(t.CampaignId, encounter.Id);
            }, BroadcastResyncAsync);
        }

        /// <summary>Soft-deletes a stored encounter (the one running on the table has to be ended first).</summary>
        public async Task DeleteEncounterAsync(TableContext ctx, string encounterId)
        {
            RequireDm(ctx);
            var table = await _repository.GetByIdAsync(ctx.TableId) ?? throw Fail("This table no longer exists.");
            if (table.Encounter?.Id == encounterId) throw Fail("End the encounter before deleting it.");

            var encounter = await _encounters.GetByIdAsync(encounterId) ?? throw Fail("Encounter not found.");
            if (encounter.CampaignId != table.CampaignId) throw Fail("That encounter belongs to another campaign.");

            await _encounters.LogicDeleteAsync(encounterId);
            // Don't leave the campaign pointing at a deleted encounter.
            var campaign = await _campaigns.GetByIdAsync(table.CampaignId);
            if (campaign?.ActiveEncounterId == encounterId) await SetActiveEncounterAsync(table.CampaignId, null);
        }

        /// <summary>Writes the table's board onto its running encounter, completes it and detaches it from the table.</summary>
        private async Task StoreActiveEncounterAsync(Tabletop t)
        {
            if (t.Encounter == null) return;

            var encounter = await _encounters.GetByIdAsync(t.Encounter.Id);
            if (encounter != null)
            {
                encounter.Board = new StoredBoard
                {
                    Grid = t.Grid,
                    Map = t.Map,
                    Fog = t.Fog,
                    Tokens = t.Tokens,
                    Templates = t.Templates,
                    Strokes = t.Strokes,
                };
                // Who took part, so the encounter reads well in the dashboard too.
                encounter.Entities = t.Tokens
                    .Where(x => x.Layer != TableLayer.Map)
                    .Select(x => new EncounterEntity
                    {
                        Type = x.CharacterId != null ? EncounterEntityType.PlayerCharacter : EncounterEntityType.Enemy,
                        ReferenceId = x.CharacterId,
                        Name = x.Name,
                        Initiative = x.Initiative,
                        Status = x.MaxHp > 0 && x.Hp <= 0 ? EncounterEntityStatus.Dead : EncounterEntityStatus.Alive,
                    })
                    .ToList();
                encounter.Status = EncounterStatus.Completed;
                encounter.EndedAt = DateTime.UtcNow;
                encounter.UpdatedAt = DateTime.UtcNow;
                await _encounters.UpdateAsync(encounter);
            }

            t.Encounter = null;
            t.Turn = new TableTurn();
            foreach (var token in t.Tokens) token.Economy = new TurnEconomy();
            await SetActiveEncounterAsync(t.CampaignId, null);
        }

        private static void ApplyBoard(Tabletop t, StoredBoard board)
        {
            t.Grid = board.Grid ?? t.Grid;
            t.Map = board.Map ?? new MapLayer();
            t.Fog = board.Fog ?? new FogState();
            t.Tokens = board.Tokens ?? new List<TableToken>();
            t.Templates = board.Templates ?? new List<AoeTemplate>();
            t.Strokes = board.Strokes ?? new List<TableStroke>();
            t.Turn = new TableTurn();
            foreach (var token in t.Tokens) token.Economy = new TurnEconomy();
        }

        /// <summary>Keeps the campaign's "active encounter" (shown in the dashboard) in step with the table.</summary>
        private async Task SetActiveEncounterAsync(string campaignId, string? encounterId)
        {
            var campaign = await _campaigns.GetByIdAsync(campaignId);
            if (campaign == null || campaign.ActiveEncounterId == encounterId) return;
            campaign.ActiveEncounterId = encounterId;
            await _campaigns.UpdateAsync(campaign);
        }

        private Task BroadcastResyncAsync(Tabletop t) => Task.WhenAll(
            _hub.Clients.Group(TabletopGroups.Dm(t.Id!))
                .SendAsync("Resync", new TableResync(Snapshot(t, isDm: true), t.Strokes, t.Fog, t.Log)),
            _hub.Clients.Group(TabletopGroups.Players(t.Id!))
                .SendAsync("Resync", new TableResync(
                    Snapshot(t, isDm: false),
                    t.Strokes.Where(s => s.Layer != TableLayer.Dm).ToList(),
                    t.Fog,
                    t.Log.Where(l => !l.Private).ToList())));

        // ──────────────────────────────────────────────
        // Character sync
        // ──────────────────────────────────────────────

        public async Task SyncCharacterAsync(Character character)
        {
            if (character?.Id == null || string.IsNullOrEmpty(character.CampaignId)) return;

            var table = await _repository.GetByCampaignIdAsync(character.CampaignId);
            if (table == null || !table.Tokens.Any(x => x.CharacterId == character.Id)) return;

            await Change(table.Id!, t =>
            {
                foreach (var token in t.Tokens.Where(x => x.CharacterId == character.Id))
                    ApplyCharacter(token, character);
            });
        }

        private static void ApplyCharacter(TableToken token, Character character)
        {
            token.Name = Text(character.Name, 60, token.Name);
            if (!string.IsNullOrWhiteSpace(character.ImageUrl)) token.ImageUrl = character.ImageUrl;
            token.Hp = character.HitPoints ?? 0;
            token.MaxHp = character.MaxHitPoints ?? 0;
            token.TempHp = character.TemporaryHitPoints ?? 0;
            token.Ac = character.ArmorClass ?? 10;
            token.Speed = character.Speed ?? 30;
            token.OwnerIds = character.OwnerIds?.ToList() ?? new List<string>();

            // Conditions map to effects by label; keep durations the table already tracks.
            var known = token.Effects
                .GroupBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            token.Effects = (character.Conditions ?? new List<string>())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .Select(label => known.TryGetValue(label, out var effect) ? effect : new TableEffect { Label = Text(label, 40, "Effect") })
                .ToList();
        }

        /// <summary>The token fields that live on the character sheet.</summary>
        private static string CharacterFields(TableToken t) =>
            $"{t.Hp}|{t.MaxHp}|{t.TempHp}|{t.Ac}|{string.Join(",", t.Effects.Select(e => e.Label))}";

        /// <summary>DM edits to a character token go to the sheet too, and the owners' clients hear about it.</summary>
        private async Task WriteThroughAsync(TableContext ctx, Tabletop t, TableToken token)
        {
            var character = await _characters.GetByIdAsync(token.CharacterId!);
            if (character == null) return;

            character.HitPoints = token.Hp;
            character.MaxHitPoints = token.MaxHp;
            character.TemporaryHitPoints = token.TempHp;
            character.ArmorClass = token.Ac;
            character.Conditions = token.Effects.Select(e => e.Label).ToList();
            character.UpdatedAt = DateTime.UtcNow;
            await _characters.UpdateAsync(character);

            var campaign = await _campaigns.GetByIdAsync(t.CampaignId);
            var recipients = (character.OwnerIds ?? new List<string>())
                .Concat(campaign?.OwnerIds ?? new List<string>())
                .Distinct()
                .ToList();

            // Same payload CharacterController broadcasts, so the existing client handler applies it.
            await _entitySync.BroadcastToUsers(
                "EntityChanged",
                new
                {
                    entityType = "Character",
                    entityId = character.Id,
                    action = "updated",
                    data = character,
                    changedBy = ctx.Name,
                    timestamp = DateTime.UtcNow,
                },
                recipients,
                excludeUserId: ctx.UserId);
        }

        // ──────────────────────────────────────────────
        // Images
        // ──────────────────────────────────────────────

        public async Task<string> UploadImageAsync(string tableId, TableCaller caller, Stream content, string fileName)
        {
            var table = await _repository.GetByIdAsync(tableId) ?? throw new NotFoundException("Table not found.");
            var campaign = await _campaigns.GetByIdAsync(table.CampaignId);
            if (campaign == null || !IsDm(campaign, caller))
                throw new UnauthorizedAccessException("Only the DM can upload table images.");

            var contentType = SniffImageType(content)
                ?? throw new ArgumentException("Only PNG, JPEG, WebP or GIF images are allowed.");

            var id = await _repository.UploadImageAsync(Text(fileName, 100, "image"), content, contentType);
            return ImagePathPrefix + id;
        }

        public Task<(Stream Content, string ContentType)?> OpenImageAsync(string imageId) =>
            _repository.OpenImageAsync(imageId);

        /// <summary>Trust the bytes, not the client's content type (SVG and HTML stay out).</summary>
        private static string? SniffImageType(Stream content)
        {
            Span<byte> head = stackalloc byte[12];
            var read = content.Read(head);
            content.Position = 0;
            if (read < 12) return null;

            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return "image/png";
            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "image/jpeg";
            if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8') return "image/gif";
            if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P') return "image/webp";
            return null;
        }

        // ──────────────────────────────────────────────
        // Plumbing
        // ──────────────────────────────────────────────

        private Task<Tabletop> Change(string tableId, Action<Tabletop> apply, Func<Tabletop, Task>? announce = null) =>
            ChangeAsync(tableId, t =>
            {
                apply(t);
                return Task.CompletedTask;
            }, announce);

        private async Task<Tabletop> ChangeAsync(string tableId, Func<Tabletop, Task> apply, Func<Tabletop, Task>? announce = null)
        {
            var gate = Locks.GetOrAdd(tableId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var table = await _repository.GetByIdAsync(tableId) ?? throw Fail("This table no longer exists.");
                await apply(table);
                table.UpdatedAt = DateTime.UtcNow;
                await _repository.ReplaceAsync(table);
                await (announce ?? BroadcastStateAsync)(table);
                return table;
            }
            finally
            {
                gate.Release();
            }
        }

        private Task BroadcastStateAsync(Tabletop t) => Task.WhenAll(
            _hub.Clients.Group(TabletopGroups.Dm(t.Id!)).SendAsync("State", Snapshot(t, isDm: true)),
            _hub.Clients.Group(TabletopGroups.Players(t.Id!)).SendAsync("State", Snapshot(t, isDm: false)));

        private Task SendFog(Tabletop t) => SendAll(t, "FogChanged", t.Fog);

        private Task SendAll(Tabletop t, string eventName, params object?[] args) =>
            _hub.Clients.Group(TabletopGroups.All(t.Id!)).SendCoreAsync(eventName, args);

        private static TableSnapshot Snapshot(Tabletop t, bool isDm)
        {
            foreach (var token in t.Tokens) token.Health = HealthOf(token);
            if (isDm) return new(t.Grid, t.Map, t.Tokens, t.Turn, t.Templates, t.Encounter);

            // The DM layer stays DM-only, and so do templates pinned to DM-layer tokens (they'd give the position away).
            var dmOnly = t.Tokens.Where(x => x.Layer == TableLayer.Dm).Select(x => x.Id).ToHashSet();
            return new(
                t.Grid,
                t.Map,
                t.Tokens.Where(x => x.Layer != TableLayer.Dm).Select(ForPlayers).ToList(),
                t.Turn,
                t.Templates.Where(x => x.Layer != TableLayer.Dm && (x.TokenId == null || !dmOnly.Contains(x.TokenId))).ToList(),
                t.Encounter);
        }

        /// <summary>Players get the numbers for player characters only; monsters and NPCs show just a health word.</summary>
        private static TableToken ForPlayers(TableToken token)
        {
            if (token.CharacterId != null) return token;
            var copy = token.ShallowCopy();
            copy.Hp = copy.MaxHp = copy.TempHp = copy.Ac = copy.Speed = 0;
            return copy;
        }

        private static string? HealthOf(TableToken t) =>
            t.MaxHp <= 0 ? null
            : t.Hp <= 0 ? "Down"
            : t.Hp * 2 <= t.MaxHp ? "Bloodied"
            : t.Hp < t.MaxHp ? "Wounded"
            : "Healthy";

        /// <summary>Players always draw on the token layer; the DM picks.</summary>
        private static TableLayer LayerFor(TableContext ctx, TableLayer requested) =>
            ctx.IsDm && Enum.IsDefined(requested) ? requested : TableLayer.Token;

        private static bool IsDm(Campaign campaign, TableCaller caller) =>
            caller.IsAdmin || (campaign.OwnerIds?.Contains(caller.UserId) ?? false);

        private static void RequireDm(TableContext ctx)
        {
            if (!ctx.IsDm) throw Fail("Only the DM can do that.");
        }

        private static bool Owns(TableContext ctx, TableToken token) => token.OwnerIds.Contains(ctx.UserId);

        private static bool IsTurnOf(Tabletop t, TableToken token) => t.Turn.Active && t.Turn.CurrentTokenId == token.Id;

        private static TableToken FindToken(Tabletop t, string tokenId) =>
            t.Tokens.FirstOrDefault(x => x.Id == tokenId) ?? throw Fail("Token not found.");

        private static HubException Fail(string message) => new(message);

        private static string Text(string? value, int max, string fallback)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return fallback;
            return trimmed.Length > max ? trimmed[..max] : trimmed;
        }

        private static string Color(string? value, string fallback) =>
            value != null && ColorRegex.IsMatch(value) ? value : fallback;

        private static string? ImageUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value.Length > 1000) throw Fail("Image URL is too long.");
            if (OwnImageRegex.IsMatch(value)) return value;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                return value;
            throw Fail("Images must be uploaded or use an http(s) URL.");
        }

        private static double Finite(double value, double fallback) =>
            double.IsFinite(value) ? value : fallback;

        private static double Coord(double value)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > MaxCoord) throw Fail("Position is off the board.");
            return value;
        }

        private static List<double> Coords(List<double>? points, int min, int max)
        {
            if (points == null || points.Count < min || points.Count > max || points.Count % 2 != 0)
                throw Fail("Invalid shape.");
            foreach (var p in points) Coord(p);
            return points;
        }
    }
}
