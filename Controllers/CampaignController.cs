using dndhelper.Authentication.Interfaces;
using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CampaignController : ControllerBase
    {
        private readonly ICampaignService _campaignService;
        private readonly IAuthService _authService;

        public CampaignController(ICampaignService service, IAuthService authService)
        {
            _campaignService = service;
            _authService = authService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var campaigns = await _campaignService.GetAllAsync();
            return Ok(campaigns);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var campaign = await _campaignService.GetByIdAsync(id);
            if (campaign == null) return NotFound();
            return Ok(campaign);
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] Campaign campaign)
        {
            string userId = _authService.GetUserIdFromToken();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("User ID not found in token.");

            try
            {
                var created = await _campaignService.CreateAsync(campaign, userId);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, Campaign campaign)
        {
            if (id != campaign.Id) return BadRequest();
            
            var updated = await _campaignService.UpdateAsync(campaign);
            if (updated == null) return NotFound();
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            string userId = _authService.GetUserIdFromToken();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("User ID not found in token.");

            try
            {
                var success = await _campaignService.DeleteAsync(id, userId);
                return success ? Ok() : NotFound();
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // ------------------------
        // PLAYER MANAGEMENT
        // ------------------------
        [HttpGet("{id}/players")]
        public async Task<IActionResult> GetCharacters(string id)
        {
            var result = await _campaignService.GetCharactersAsync(id);

            return result == null ? NotFound() : Ok(result);
        }

        [HttpPost("{id}/players/{playerId}")]
        public async Task<IActionResult> AddPlayer(string id, string playerId)
        {
            var result = await _campaignService.AddCharacterAsync(id, playerId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpDelete("{id}/players/{playerId}")]
        public async Task<IActionResult> RemovePlayer(string id, string playerId)
        {
            var result = await _campaignService.RemoveCharacterAsync(id, playerId);
            return result == null ? NotFound() : Ok(result);
        }

        // ------------------------
        // WORLD MANAGEMENT
        // ------------------------
        [HttpPost("{id}/worlds/{worldId}")]
        public async Task<IActionResult> AddWorld(string id, string worldId)
        {
            var result = await _campaignService.AddWorldAsync(id, worldId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpDelete("{id}/worlds/{worldId}")]
        public async Task<IActionResult> RemoveWorld(string id, string worldId)
        {
            var result = await _campaignService.RemoveWorldAsync(id, worldId);
            return result == null ? NotFound() : Ok(result);
        }

        // ------------------------
        // QUEST MANAGEMENT
        // ------------------------
        [HttpPost("{id}/quests/{questId}")]
        public async Task<IActionResult> AddQuest(string id, string questId)
        {
            var result = await _campaignService.AddQuestAsync(id, questId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpDelete("{id}/quests/{questId}")]
        public async Task<IActionResult> RemoveQuest(string id, string questId)
        {
            var result = await _campaignService.RemoveQuestAsync(id, questId);
            return result == null ? NotFound() : Ok(result);
        }

        // ------------------------
        // NOTE MANAGEMENT
        // ------------------------
        [HttpPost("{id}/notes/{noteId}")]
        public async Task<IActionResult> AddNote(string id, string noteId)
        {
            var result = await _campaignService.AddNoteAsync(id, noteId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpDelete("{id}/notes/{noteId}")]
        public async Task<IActionResult> RemoveNote(string id, string noteId)
        {
            var result = await _campaignService.RemoveNoteAsync(id, noteId);
            return result == null ? NotFound() : Ok(result);
        }

        // ------------------------
        // SESSION MANAGEMENT
        // ------------------------
        [HttpPost("{id}/sessions/{sessionId}")]
        public async Task<IActionResult> AddSession(string id, string sessionId)
        {
            var result = await _campaignService.AddSessionAsync(id, sessionId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpDelete("{id}/sessions/{sessionId}")]
        public async Task<IActionResult> RemoveSession(string id, string sessionId)
        {
            var result = await _campaignService.RemoveSessionAsync(id, sessionId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpPut("{id}/current-session/{sessionId}")]
        public async Task<IActionResult> SetCurrentSession(string id, string sessionId)
        {
            var result = await _campaignService.SetCurrentSessionAsync(id, sessionId);
            return result == null ? NotFound() : Ok(result);
        }

    }
}
