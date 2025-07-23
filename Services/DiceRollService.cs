using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class DiceRollService : IDiceRollService
    {
        private readonly Random _random;
        private readonly ILogger _logger;

        public DiceRollService(ILogger logger)
        {
            _random = new Random();
            _logger = logger;
        }

        public async Task<(List<Die> Rolls, int Total)> RollDiceAsync(int numberOfDice, int sides)
        {
            if (numberOfDice <= 0 || sides <= 0)
            {
                _logger.Warning($"Invalid dice roll request: numberOfDice={numberOfDice}, sides={sides} ⚔");
                throw new ArgumentException("Number of dice and sides must be positive.");
            }

            var rolls = new List<Die>();
            int total = 0;
            for (int i = 0; i < numberOfDice; i++)
            {
                int result = _random.Next(1, sides + 1);
                rolls.Add(new Die { Sides = sides, Result = result });
                total += result;
            }

            _logger.Information($"Rolled {numberOfDice}d{sides}: {string.Join(", ", rolls.Select(r => r.Result))}, Total: {total} 🎲");
            return await Task.FromResult((rolls, total));
        }
    }
}
