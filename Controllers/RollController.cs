using dndhelper.Models.RollModels;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class RollController : ControllerBase
    {
        private readonly ISubtleRollService _subtleRollService;

        public RollController(ISubtleRollService subtleRollService)
        {
            _subtleRollService = subtleRollService;
        }

        [HttpPost("subtle")]
        public async Task<IActionResult> SubtleRoll([FromBody] SubtleRollRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            try
            {
                var receipt = await _subtleRollService.RollSubtleAsync(request);
                return Ok(receipt);
            }
            catch (RateLimitException ex)
            {
                return StatusCode(429, new { message = ex.Message });
            }
        }
    }
}
