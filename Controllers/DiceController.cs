using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Models.RollModels;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api")]
    public class DiceController : ControllerBase
    {
        private readonly IDiceRollService _diceRollService;
        private readonly IAuthService _authorizationService;
        private readonly IRollHistoryService _rollHistoryService;
        private readonly ICharacterService _characterService;
        private readonly IMemoryCache _cache;
        private readonly DiceRollOptions _options;

        public DiceController(
            IDiceRollService diceRollService,
            IAuthService authorizationService,
            IRollHistoryService rollHistoryService,
            ICharacterService characterService,
            IMemoryCache cache,
            IOptions<DiceRollOptions> options)
        {
            _diceRollService = diceRollService;
            _authorizationService = authorizationService;
            _rollHistoryService = rollHistoryService;
            _characterService = characterService;
            _cache = cache;
            _options = options?.Value ?? new DiceRollOptions();
        }

        [HttpGet("roll/{numberOfDice}d{sides}")]
        public Task<IActionResult> RollDice(int numberOfDice, int sides, [FromQuery] string? characterId) =>
            RollAsync(() => _diceRollService.RollDice(numberOfDice, sides), characterId);

        [HttpGet("roll")]
        public Task<IActionResult> RollExpression([FromQuery] string expression, [FromQuery] string? characterId)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return Task.FromResult<IActionResult>(BadRequest("Expression is required."));

            return RollAsync(() => _diceRollService.RollDice(1, 1, 0, expression), characterId);
        }

        /// <summary>A roll made as a character lands in that character's campaign roll log.</summary>
        private async Task<IActionResult> RollAsync(Func<DiceRollResult> rollDice, string? characterId)
        {
            try
            {
                User user = await _authorizationService.GetUserFromTokenAsync();
                var character = string.IsNullOrWhiteSpace(characterId)
                    ? null
                    : await _characterService.GetByIdAsync(characterId) ?? throw new NotFoundException("Character not found.");

                EnforceRateLimit(user.Id);

                var roll = rollDice();
                await _rollHistoryService.RecordAsync(new RollRecord
                {
                    UserId = user.Id,
                    Username = user.Username,
                    CharacterId = character?.Id,
                    CharacterName = character?.Name,
                    CampaignId = character?.CampaignId,
                    Type = RollType.Public,
                    Expression = roll.Expression,
                    NumberOfDice = roll.NumberOfDice,
                    Sides = roll.Sides,
                    Modifier = roll.Modifier,
                    Rolls = roll.Rolls,
                    Total = roll.Total,
                    Min = roll.Min,
                    Max = roll.Max,
                    Average = roll.Average
                });

                return Ok(new
                {
                    user.Username,
                    roll.NumberOfDice,
                    roll.Sides,
                    roll.Modifier,
                    roll.Rolls,
                    roll.Total,
                    roll.Min,
                    roll.Max,
                    roll.Average,
                    roll.Expression
                });
            }
            catch (RateLimitException ex)
            {
                return StatusCode(429, new { message = ex.Message });
            }
        }

        private void EnforceRateLimit(string userId)
        {
            Guard.NotNullOrWhiteSpace(userId, nameof(userId));

            var cacheKey = $"public-roll:{userId}";
            var window = TimeSpan.FromSeconds(Math.Max(1, _options.PublicRateLimitWindowSeconds));
            var timestamps = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.SlidingExpiration = window;
                return new System.Collections.Generic.List<DateTime>();
            });

            if (timestamps == null)
                throw new InvalidOperationException("Failed to initialize rate limit state.");

            var now = DateTime.UtcNow;
            var windowStart = now - window;

            lock (timestamps)
            {
                timestamps.RemoveAll(t => t < windowStart);

                if (timestamps.Count >= _options.PublicRateLimitMax)
                    throw new RateLimitException($"Roll limit reached. Max {_options.PublicRateLimitMax} per {window.TotalSeconds:0} seconds.");

                timestamps.Add(now);
            }
        }
    }
}
