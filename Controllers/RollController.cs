using dndhelper.Models.RollModels;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using dndhelper.Authentication.Interfaces;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class RollController : ControllerBase
    {
        private readonly ISubtleRollService _subtleRollService;
        private readonly IRollHistoryService _rollHistoryService;
        private readonly IAuthService _authService;

        public RollController(
            ISubtleRollService subtleRollService,
            IRollHistoryService rollHistoryService,
            IAuthService authService)
        {
            _subtleRollService = subtleRollService;
            _rollHistoryService = rollHistoryService;
            _authService = authService;
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

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] string? campaignId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var user = await _authService.GetUserFromTokenAsync();
            if (user == null)
                return Unauthorized();

            if (!string.IsNullOrWhiteSpace(campaignId))
            {
                if (user.CampaignIds == null || !user.CampaignIds.Contains(campaignId))
                    return Forbid();

                var rolls = await _rollHistoryService.GetRollsByCampaignAsync(campaignId, page, pageSize);
                return Ok(rolls);
            }

            var myRolls = await _rollHistoryService.GetMyPublicRollsAsync(user.Id, page, pageSize);
            return Ok(myRolls);
        }
    }
}
