using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Models;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CharacterController : ControllerBase
{
    private readonly ICharacterService _characterService;
    private readonly IAuthService _authService;

    public CharacterController(ICharacterService service, IAuthService authService)
    {
        _characterService = service;
        _authService = authService;
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
        return CreatedAtAction(nameof(GetById), "Character", new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Character character)
    {
        if (id != character.Id) return BadRequest();

        var updated = await _characterService.UpdateAsync(character);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _characterService.DeleteAsync(id);
        if (!deleted) return NotFound();
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
}
