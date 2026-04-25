using dndhelper.Models;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
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
    public class EncounterController : ControllerBase
    {
        private readonly IEncounterService _encounterService;
        private readonly ILogger _logger;

        public EncounterController(
            IEncounterService encounterService,
            ILogger logger)
        {
            _encounterService = encounterService;
            _logger = logger;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(id, nameof(id));

                var encounter = await _encounterService.GetByIdAsync(id);
                if (encounter == null)
                    return NotFound(new { message = "Encounter not found." });

                return Ok(encounter);
            }
            catch (ArgumentNullException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var encounters = await _encounterService.GetAllAsync();
            return Ok(encounters);
        }

        [HttpGet("campaign/{campaignId}")]
        public async Task<IActionResult> GetByCampaign(string campaignId)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));
                var encounters = await _encounterService.GetByCampaignIdAsync(campaignId);
                return Ok(encounters);
            }
            catch (ArgumentNullException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("session/{sessionId}")]
        public async Task<IActionResult> GetBySession(string sessionId)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(sessionId, nameof(sessionId));
                var encounters = await _encounterService.GetBySessionIdAsync(sessionId);
                return Ok(encounters);
            }
            catch (ArgumentNullException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Encounter encounter)
        {
            try
            {
                Guard.NotNull(encounter, nameof(encounter));
                var created = await _encounterService.CreateAndNotifyAsync(encounter);
                if (created == null)
                    return StatusCode(500, new { message = "Failed to create encounter." });

                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (ArgumentNullException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating encounter");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Encounter encounter)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(id, nameof(id));
                Guard.NotNull(encounter, nameof(encounter));

                encounter.Id = id;
                var updated = await _encounterService.UpdateAndNotifyAsync(id, encounter);
                if (updated == null)
                    return NotFound(new { message = "Encounter not found." });

                return Ok(updated);
            }
            catch (ArgumentNullException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating encounter {EncounterId}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(id, nameof(id));
                var success = await _encounterService.DeleteAndNotifyAsync(id);
                if (!success)
                    return NotFound(new { message = "Encounter not found." });

                return NoContent();
            }
            catch (ArgumentNullException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting encounter {EncounterId}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
