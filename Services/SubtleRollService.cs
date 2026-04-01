using dndhelper.Authentication.Interfaces;
using dndhelper.Models.RollModels;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class SubtleRollService : ISubtleRollService
    {
        private readonly IDiceRollService _diceRollService;
        private readonly ICharacterService _characterService;
        private readonly ICampaignService _campaignService;
        private readonly IEntitySyncService _entitySyncService;
        private readonly IAuthService _authService;
        private readonly IRollHistoryService _rollHistoryService;
        private readonly IMemoryCache _cache;
        private readonly ILogger _logger;
        private readonly DiceRollOptions _options;

        public SubtleRollService(
            IDiceRollService diceRollService,
            ICharacterService characterService,
            ICampaignService campaignService,
            IEntitySyncService entitySyncService,
            IAuthService authService,
            IRollHistoryService rollHistoryService,
            IMemoryCache cache,
            IOptions<DiceRollOptions> options,
            ILogger logger)
        {
            _diceRollService = diceRollService ?? throw new ArgumentNullException(nameof(diceRollService));
            _characterService = characterService ?? throw new ArgumentNullException(nameof(characterService));
            _campaignService = campaignService ?? throw new ArgumentNullException(nameof(campaignService));
            _entitySyncService = entitySyncService ?? throw new ArgumentNullException(nameof(entitySyncService));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _rollHistoryService = rollHistoryService ?? throw new ArgumentNullException(nameof(rollHistoryService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? new DiceRollOptions();
        }

        public async Task<SubtleRollReceipt> RollSubtleAsync(SubtleRollRequest request)
        {
            Guard.NotNull(request, nameof(request));
            var characterId = Guard.NotNullOrWhiteSpace(request.CharacterId, nameof(request.CharacterId));
            var hasExpression = !string.IsNullOrWhiteSpace(request.Expression);
            if (!hasExpression)
            {
                Guard.GreaterThanZero(request.NumberOfDice, nameof(request.NumberOfDice));
                Guard.GreaterThanZero(request.Sides, nameof(request.Sides));
            }

            var character = await _characterService.GetByIdAsync(characterId);
            if (character == null)
                throw new KeyNotFoundException($"Character not found with id: {characterId}");

            if (string.IsNullOrWhiteSpace(character.CampaignId))
                throw new InvalidOperationException("Character is not in a campaign.");

            var dmIds = await _campaignService.GetCampaignDMIdsAsync(character.CampaignId);
            if (dmIds == null || dmIds.Count == 0)
                throw new InvalidOperationException("Campaign has no DM to receive subtle rolls.");

            var user = await _authService.GetUserFromTokenAsync();
            if (user == null)
                throw new UnauthorizedAccessException("User not found.");

            EnforceRateLimit(user.Id);

            var roll = _diceRollService.RollDice(
                request.NumberOfDice,
                request.Sides,
                0,
                request.Expression
            );

            var rollId = Guid.NewGuid().ToString("N");
            var timestamp = DateTime.UtcNow;

            var payload = new SubtleRollPayload
            {
                RollId = rollId,
                CharacterId = character.Id ?? characterId,
                CharacterName = character.Name ?? "Unknown",
                CampaignId = character.CampaignId!,
                RolledByUserId = user.Id,
                RolledByUsername = user.Username,
                NumberOfDice = roll.NumberOfDice,
                Sides = roll.Sides,
                Modifier = roll.Modifier,
                Rolls = roll.Rolls,
                Total = roll.Total,
                Min = roll.Min,
                Max = roll.Max,
                Average = roll.Average,
                Expression = roll.Expression,
                Note = request.Note,
                TimestampUtc = timestamp
            };

            await _entitySyncService.BroadcastToUsers(
                "SubtleRoll",
                payload,
                dmIds
            );

            _logger.Information(
                "Subtle roll {RollId} sent to {DmCount} DM(s) for character {CharacterId}",
                rollId,
                dmIds.Count,
                character.Id
            );

            await _rollHistoryService.CreateAsync(new RollRecord
            {
                UserId = user.Id,
                Username = user.Username,
                CharacterId = character.Id,
                CampaignId = character.CampaignId,
                Type = RollType.Subtle,
                Expression = roll.Expression,
                NumberOfDice = roll.NumberOfDice,
                Sides = roll.Sides,
                Modifier = roll.Modifier,
                Rolls = roll.Rolls,
                Total = roll.Total,
                Min = roll.Min,
                Max = roll.Max,
                Average = roll.Average,
                Note = request.Note
            });

            return new SubtleRollReceipt
            {
                RollId = rollId,
                TimestampUtc = timestamp,
                DmCount = dmIds.Count
            };
        }

        private void EnforceRateLimit(string userId)
        {
            Guard.NotNullOrWhiteSpace(userId, nameof(userId));

            var cacheKey = $"subtle-roll:{userId}";
            var window = TimeSpan.FromSeconds(Math.Max(1, _options.SubtleRateLimitWindowSeconds));
            var timestamps = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.SlidingExpiration = window;
                return new List<DateTime>();
            });

            if (timestamps == null)
                throw new InvalidOperationException("Failed to initialize rate limit state.");

            var now = DateTime.UtcNow;
            var windowStart = now - window;

            lock (timestamps)
            {
                timestamps.RemoveAll(t => t < windowStart);

                if (timestamps.Count >= _options.SubtleRateLimitMax)
                {
                    throw new RateLimitException(
                        $"Subtle roll limit reached. Max {_options.SubtleRateLimitMax} per {window.TotalSeconds:0} seconds."
                    );
                }

                timestamps.Add(now);
            }
        }
    }
}
