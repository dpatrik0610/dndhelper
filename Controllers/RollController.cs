using dndhelper.Models.RollModels;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using dndhelper.Authentication.Interfaces;
using dndhelper.Authorization;

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

        /// <summary>
        /// Roll log for a campaign (query, else X-Campaign-Id): DMs see every roll, players the party's public rolls plus
        /// their own subtle ones. With no campaign selected, the caller's own rolls.
        /// </summary>
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromServices] CampaignAccess access, [FromQuery] string? campaignId, [FromQuery] RollType? type, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var user = await _authService.GetUserFromTokenAsync();
            if (user == null)
                return Unauthorized();

            campaignId ??= access.CurrentCampaignId;
            if (campaignId != null)
                await access.EnsureMemberAsync(campaignId);
            var isDm = campaignId != null && await access.IsDmAsync(campaignId);

            return Ok(await _rollHistoryService.GetHistoryAsync(user.Id, campaignId, isDm, type, page, pageSize));
        }
    }
}
