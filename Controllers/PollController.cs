using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    /// <summary>Errors (not found, forbidden, validation) are mapped by ExceptionMiddleware.</summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PollController : ControllerBase
    {
        private readonly IPollService _polls;

        public PollController(IPollService polls)
        {
            _polls = polls;
        }

        [HttpGet("campaign/{campaignId}")]
        public async Task<IActionResult> GetByCampaign(string campaignId) => Ok(await _polls.GetByCampaignAsync(campaignId));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id) => Ok(await _polls.GetPollAsync(id));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PollUpsertRequest request) => Ok(await _polls.CreatePollAsync(request));

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] PollUpsertRequest request) => Ok(await _polls.UpdatePollAsync(id, request));

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id) =>
            await _polls.DeletePollAsync(id) ? NoContent() : NotFound(new { message = "Poll not found." });

        [HttpPost("{id}/vote")]
        public async Task<IActionResult> Vote(string id, [FromBody] PollVoteRequest request) => Ok(await _polls.VoteAsync(id, request));

        [HttpDelete("{id}/vote")]
        public async Task<IActionResult> RetractVote(string id) => Ok(await _polls.RetractVoteAsync(id));

        [HttpPost("{id}/options")]
        public async Task<IActionResult> SuggestOption(string id, [FromBody] PollSuggestionRequest request) =>
            Ok(await _polls.SuggestOptionAsync(id, request));

        [HttpPost("{id}/close")]
        public async Task<IActionResult> Close(string id) => Ok(await _polls.ClosePollAsync(id));

        [HttpPost("{id}/reopen")]
        public async Task<IActionResult> Reopen(string id) => Ok(await _polls.ReopenPollAsync(id));

        [HttpPost("{id}/announce")]
        public async Task<IActionResult> Announce(string id)
        {
            await _polls.AnnounceAsync(id);
            return NoContent();
        }
    }
}
