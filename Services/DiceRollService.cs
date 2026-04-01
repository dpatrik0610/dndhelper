using dndhelper.Models.RollModels;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.Collections.Generic;

namespace dndhelper.Services
{
    public class DiceRollService : IDiceRollService
    {
        private readonly ILogger _logger;
        private readonly DiceRollOptions _options;

        public DiceRollService(ILogger logger, IOptions<DiceRollOptions> options)
        {
            _logger = logger;
            _options = options?.Value ?? new DiceRollOptions();
        }

        public DiceRollResult RollDice(int numberOfDice, int sides, int modifier = 0, string? expression = null)
        {
            if (!string.IsNullOrWhiteSpace(expression))
            {
                var parsed = DiceExpressionParser.Parse(expression);
                numberOfDice = parsed.NumberOfDice;
                sides = parsed.Sides;
                modifier = parsed.Modifier;
                expression = parsed.Normalized;
            }

            Guard.GreaterThanZero(numberOfDice, nameof(numberOfDice));
            Guard.GreaterThanZero(sides, nameof(sides));
            Guard.InRange(numberOfDice, 1, _options.MaxDice, nameof(numberOfDice));
            Guard.InRange(sides, 1, _options.MaxSides, nameof(sides));

            var rolls = new List<int>(numberOfDice);
            int total = 0;
            for (int i = 0; i < numberOfDice; i++)
            {
                int result = Random.Shared.Next(1, sides + 1);
                rolls.Add(result);
                total += result;
            }

            total += modifier;

            var normalized = string.IsNullOrWhiteSpace(expression)
                ? (modifier == 0 ? $"{numberOfDice}d{sides}" : $"{numberOfDice}d{sides}{(modifier > 0 ? "+" : "-")}{Math.Abs(modifier)}")
                : expression;

            var min = numberOfDice + modifier;
            var max = (numberOfDice * sides) + modifier;
            var average = numberOfDice * (sides + 1) / 2.0 + modifier;

            _logger.Debug(
                "Rolled {NumberOfDice}d{Sides}: {Rolls} (Total: {Total})",
                numberOfDice,
                sides,
                string.Join(", ", rolls),
                total
            );

            return new DiceRollResult
            {
                NumberOfDice = numberOfDice,
                Sides = sides,
                Modifier = modifier,
                Rolls = rolls,
                Total = total,
                Min = min,
                Max = max,
                Average = average,
                Expression = normalized,
                TimestampUtc = DateTime.UtcNow
            };
        }
    }
}
