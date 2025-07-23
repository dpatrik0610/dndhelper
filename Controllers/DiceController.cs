using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [ApiController]
    [Route("api")]
    public class DiceController : ControllerBase
    {
        private readonly IDiceRollService _diceRollService;

        public DiceController(IDiceRollService diceRollService)
        {
            _diceRollService = diceRollService;
        }

        [HttpGet("roll/{numberOfDice}d{sides}")]
        public async Task<IActionResult> RollDice(int numberOfDice, int sides)
        {
            var (rolls, total) = await _diceRollService.RollDiceAsync(numberOfDice, sides);
            return Ok(new { NumberOfDice = numberOfDice, Sides = sides, Rolls = rolls.Select(x => x.Result), Total = total });
        }
    }
}
