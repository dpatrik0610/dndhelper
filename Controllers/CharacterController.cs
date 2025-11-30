using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Models.CharacterModels;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CharacterController : ControllerBase
{
    private readonly ICharacterService _characterService;
    private readonly IEntitySyncService _entitySyncService;
    private readonly IAuthService _authService;
    private readonly ICampaignService _campaignService;

    public CharacterController(ICharacterService service, IEntitySyncService entitySyncService, IAuthService authService, ICampaignService campaignService)
    {
        _characterService = service;
        _entitySyncService = entitySyncService;
        _authService = authService;
        _campaignService = campaignService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
       var characters = await _characterService.GetAllAsync();
        return Ok(characters);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var character = await _characterService.GetByIdAsync(id);
        if (character == null) return NotFound();
        return Ok(character);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Character character)
    {
        var created = await _characterService.CreateAsync(character);
        if (string.IsNullOrEmpty(created?.Id)) 
            return StatusCode(500, "Server side error at character creation.");


        await BroadcastCharacterChangeAsync(created, "created", created);

        return CreatedAtAction(nameof(GetById), "Character", new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Character character)
    {
        if (id != character.Id)
            return BadRequest();

        var updated = await _characterService.UpdateAsync(character);
        if (updated == null)
            return NotFound();

        await BroadcastCharacterChangeAsync(updated, "updated", updated);

        return Ok(updated);
    }


    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var character = await _characterService.GetByIdAsync(id);
        if (character == null)
            return NotFound();

        var deleted = await _characterService.DeleteAsync(id);
        if (!deleted)
            return StatusCode(500, "Error deleting character.");

        await BroadcastCharacterChangeAsync(character, "deleted", data: null);

        return NoContent();
    }


    [HttpGet("own")]
    public async Task<IActionResult> GetForCurrentUser()
    {
        User user = await _authService.GetUserFromTokenAsync();
        if (user == null || user.CharacterIds.IsNullOrEmpty())
            return Ok(Enumerable.Empty<Character>());

        var characters = await _characterService.GetByIdsAsync(user.CharacterIds!);
        return Ok(characters);
    }


    [HttpPost("{id}/spellslots/use/{level}")]
    public async Task<IActionResult> UseSpellSlot(string id, int level)
    {
        var success = await _characterService.UseSpellSlotAsync(id, level);
        if (!success)
            return BadRequest("Cannot use spell slot.");

        var updated = await _characterService.GetByIdAsync(id);
        if (updated != null)
            await BroadcastCharacterChangeAsync(updated, "updated", updated);

        return Ok();
    }

    [HttpPost("{id}/spellslots/recover/{level}")]
    public async Task<IActionResult> RecoverSpellSlot(string id, int level, [FromQuery] int amount = 1)
    {
        var success = await _characterService.RecoverSpellSlotAsync(id, level, amount);
        if (!success)
            return BadRequest("Cannot recover spell slot.");

        var updated = await _characterService.GetByIdAsync(id);
        if (updated != null)
            await BroadcastCharacterChangeAsync(updated, "updated", updated);

        return Ok();
    }

    [HttpPost("{id}/longrest")]
    public async Task<IActionResult> LongRest(string id)
    {
        var success = await _characterService.LongRestAsync(id);
        if (!success)
            return BadRequest("Cannot perform long rest.");

        var updated = await _characterService.GetByIdAsync(id);
        if (updated != null)
            await BroadcastCharacterChangeAsync(updated, "updated", updated);

        return Ok();
    }

    private async Task BroadcastCharacterChangeAsync(Character character, string action, object data)
    {
        if (character.OwnerIds == null || !character.OwnerIds.Any())
            return;

        var user = await _authService.GetUserFromTokenAsync();
        var recipients = new HashSet<string>(character.OwnerIds);

        if (!string.IsNullOrEmpty(character.CampaignId))
        {
            var dmIds = await _campaignService.GetCampaignDMIdsAsync(character.CampaignId);
            if (dmIds != null)
            {
                foreach (var dmId in dmIds)
                    recipients.Add(dmId);
            }
        }

        await _entitySyncService.BroadcastToUsers(
            "EntityChanged",
            new
            {
                entityType = "Character",
                entityId = character.Id,
                action,
                data,
                changedBy = user.Username,
                timestamp = DateTime.UtcNow,
            },
            recipients.ToList(),
            excludeUserId: user.Id
        );
    }
}
