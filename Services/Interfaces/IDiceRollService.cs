using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IDiceRollService
    {
        Task<(List<Die> Rolls, int Total)> RollDiceAsync(int numberOfDice, int sides);
    }
}
