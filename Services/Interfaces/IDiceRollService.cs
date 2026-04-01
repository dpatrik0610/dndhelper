using dndhelper.Models.RollModels;

namespace dndhelper.Services.Interfaces
{
    public interface IDiceRollService
    {
        DiceRollResult RollDice(int numberOfDice, int sides, int modifier = 0, string? expression = null);
    }
}
