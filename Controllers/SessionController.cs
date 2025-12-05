using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SessionController : ControllerBase
    {
        private readonly ISessionService _sessionService;
        private readonly ILogger _logger;

        public SessionController(
            ISessionService sessionService,
            ILogger logger)
        {
            _sessionService = sessionService;
            _logger = logger;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Session ID is required." });

            var session = await _sessionService.GetByIdAsync(id);
            if (session == null)
                return NotFound(new { message = "Session not found." });

            return Ok(session);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var sessions = await _sessionService.GetAllAsync();
            return Ok(sessions);
        }

        [HttpGet("campaign/{campaignId}")]
        public async Task<IActionResult> GetByCampaign(string campaignId)
        {
            if (string.IsNullOrWhiteSpace(campaignId))
                return BadRequest(new { message = "Campaign ID is required." });

            var sessions = await _sessionService.GetByCampaignIdAsync(campaignId);
            return Ok(sessions);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Session session)
        {
            if (session == null)
                return BadRequest(new { message = "Session payload is required." });

            try
            {
                var created = await _sessionService.CreateAndNotifyAsync(session);
                if (created == null)
                    return StatusCode(500, new { message = "Failed to create session." });

                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating session");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Session session)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Session ID is required." });

            if (session == null)
                return BadRequest(new { message = "Session payload is required." });

            session.Id = id;

            try
            {
                var updated = await _sessionService.UpdateAndNotifyAsync(id, session);
                if (updated == null)
                    return NotFound(new { message = "Session not found." });

                return Ok(updated);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating session {SessionId}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Session ID is required." });

            try
            {
                var success = await _sessionService.DeleteAndNotifyAsync(id);
                if (!success)
                    return NotFound(new { message = "Session not found." });

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting session {SessionId}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

    }
}
