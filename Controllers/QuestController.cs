using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class QuestController : ControllerBase
    {
        private readonly IQuestService _questService;
        private readonly ILogger _logger;

        public QuestController(IQuestService questService, ILogger logger)
        {
            _questService = questService;
            _logger = logger;
        }

        // GET: api/quest/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Quest ID is required." });

            try
            {
                var quest = await _questService.GetByIdAsync(id);
                if (quest == null)
                    return NotFound(new { message = "Quest not found." });

                return Ok(new { data = quest, message = "Quest retrieved successfully." });
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Error retrieving quest {Id}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // GET: api/quest/campaign/{campaignId}
        [HttpGet("campaign/{campaignId}")]
        public async Task<IActionResult> GetByCampaignId(string campaignId)
        {
            if (string.IsNullOrWhiteSpace(campaignId))
                return BadRequest(new { message = "Campaign ID is required." });

            try
            {
                var quests = await _questService.GetByCampaignIdAsync(campaignId);
                return Ok(new { data = quests, message = "Quests retrieved successfully." });
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Error retrieving quests for campaign {CampaignId}", campaignId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // POST: api/quest
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Quest quest)
        {
            if (quest == null)
                return BadRequest(new { message = "Invalid quest payload." });

            try
            {
                var created = await _questService.CreateAndNotifyAsync(quest);
                if (created == null)
                    return StatusCode(500, new { message = "Failed to create quest." });

                return Ok(new { data = created, message = "Quest created successfully." });
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Error creating quest");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // PUT: api/quest/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Quest quest)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Quest ID is required." });

            if (quest == null)
                return BadRequest(new { message = "Invalid quest payload." });

            try
            {
                var updated = await _questService.UpdateAndNotifyAsync(id, quest);
                if (updated == null)
                    return NotFound(new { message = "Quest not found." });

                return Ok(new { data = updated, message = "Quest updated successfully." });
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Error updating quest {QuestId}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // DELETE: api/quest/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Quest ID is required." });

            try
            {
                var success = await _questService.DeleteAndNotifyAsync(id);
                if (!success)
                    return NotFound(new { message = "Quest not found or could not be deleted." });

                return Ok(new { message = "Quest deleted successfully." });
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Error deleting quest {QuestId}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // POST: api/quest/{questId}/objective
        [HttpPost("{questId}/objective")]
        public async Task<IActionResult> AddObjective(string questId, [FromBody] QuestObjective objective)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return BadRequest(new { message = "Quest ID is required." });

            if (objective == null)
                return BadRequest(new { message = "Invalid objective payload." });

            try
            {
                var updatedQuest = await _questService.AddObjectiveAsync(questId, objective);
                if (updatedQuest == null)
                    return NotFound(new { message = "Quest not found." });

                return Ok(new { data = updatedQuest, message = "Objective added successfully." });
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Error adding objective to quest {QuestId}", questId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // PUT: api/quest/{questId}/objective
        [HttpPut("{questId}/objective")]
        public async Task<IActionResult> UpdateObjective(string questId, [FromBody] QuestObjective objective)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return BadRequest(new { message = "Quest ID is required." });

            if (objective == null)
                return BadRequest(new { message = "Invalid objective payload." });

            try
            {
                var updatedQuest = await _questService.UpdateObjectiveAsync(questId, objective);
                if (updatedQuest == null)
                    return NotFound(new { message = "Quest or Objective not found." });

                return Ok(new { data = updatedQuest, message = "Objective updated successfully." });
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Error updating objective in quest {QuestId}", questId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // DELETE: api/quest/{questId}/objective/{objectiveId}
        [HttpDelete("{questId}/objective/{objectiveId}")]
        public async Task<IActionResult> DeleteObjective(string questId, string objectiveId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return BadRequest(new { message = "Quest ID is required." });

            if (string.IsNullOrWhiteSpace(objectiveId))
                return BadRequest(new { message = "Objective ID is required." });

            try
            {
                var updatedQuest = await _questService.DeleteObjectiveAsync(questId, objectiveId);
                if (updatedQuest == null)
                    return NotFound(new { message = "Quest not found." });

                return Ok(new { data = updatedQuest, message = "Objective deleted successfully." });
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Error deleting objective from quest {QuestId}", questId);
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
