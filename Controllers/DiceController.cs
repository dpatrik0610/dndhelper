using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Models.RollModels;
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
        private readonly IMemoryCache _cache;
        private readonly DiceRollOptions _options;

        public DiceController(
            IDiceRollService diceRollService,
            IAuthService authorizationService,
            IRollHistoryService rollHistoryService,
            IMemoryCache cache,
            IOptions<DiceRollOptions> options)
        {
            _diceRollService = diceRollService;
            _authorizationService = authorizationService;
            _rollHistoryService = rollHistoryService;
            _cache = cache;
            _options = options?.Value ?? new DiceRollOptions();
        }

        [HttpGet("roll/{numberOfDice}d{sides}")]
        public async Task<IActionResult> RollDice(int numberOfDice, int sides)
        {
            try
            {
                User user = await _authorizationService.GetUserFromTokenAsync();
                EnforceRateLimit(user.Id);

                var roll = _diceRollService.RollDice(numberOfDice, sides);
                await _rollHistoryService.CreateAsync(MapRecord(roll, user, RollType.Public));

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

        [HttpGet("roll")]
        public async Task<IActionResult> RollExpression([FromQuery] string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return BadRequest("Expression is required.");

            try
            {
                User user = await _authorizationService.GetUserFromTokenAsync();
                EnforceRateLimit(user.Id);

                var roll = _diceRollService.RollDice(1, 1, 0, expression);
                await _rollHistoryService.CreateAsync(MapRecord(roll, user, RollType.Public));

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

        private static RollRecord MapRecord(DiceRollResult roll, User user, RollType type)
        {
            return new RollRecord
            {
                UserId = user.Id,
                Username = user.Username,
                Type = type,
                Expression = roll.Expression,
                NumberOfDice = roll.NumberOfDice,
                Sides = roll.Sides,
                Modifier = roll.Modifier,
                Rolls = roll.Rolls,
                Total = roll.Total,
                Min = roll.Min,
                Max = roll.Max,
                Average = roll.Average
            };
        }
    }
}
