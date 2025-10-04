using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
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
        public DiceController(IDiceRollService diceRollService, IAuthService authorizationService)
        {
            _diceRollService = diceRollService;
            _authorizationService = authorizationService;
        }

        [HttpGet("roll/{numberOfDice}d{sides}")]
        public async Task<IActionResult> RollDice(int numberOfDice, int sides)
        {
            User user = await _authorizationService.GetUserFromTokenAsync();
            var (rolls, total) = await _diceRollService.RollDiceAsync(numberOfDice, sides);
            return Ok(
            new {   
                user.Username,
                NumberOfDice = numberOfDice,
                Sides = sides,
                Rolls = rolls.Select(x => x.Result), 
                Total = total 
            });
        }
    }
}
