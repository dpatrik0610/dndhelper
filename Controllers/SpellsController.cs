using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SpellsController : ControllerBase
    {
        private readonly ISpellService _spellService;
        private readonly ILogger _logger;

        public SpellsController(ISpellService spellService, ILogger logger)
        {
            _spellService = spellService;
            _logger = logger;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var spell = await _spellService.GetByIdAsync(id);
                if (spell == null) return NotFound();
                return Ok(spell);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("names")]
        public async Task<ActionResult<List<SpellNameResponse>>> GetAllNames()
        {
            var spells = await _spellService.GetAllNamesAsync();
            return Ok(spells);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var spells = await _spellService.GetAllAsync();
            return Ok(spells);
        }


        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Spell spell)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User ID claim missing.");

                var created = await _spellService.CreateAsync(spell);
                return CreatedAtAction(nameof(GetById), new { id = created!.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }


        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Spell spell)
        {
            if (id != spell.Id)
                return BadRequest("ID mismatch.");

            try
            {
                var updated = await _spellService.UpdateAsync(spell);
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _spellService.DeleteAsync(id);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPatch("soft-delete/{id}")]
        public async Task<IActionResult> LogicDelete(string id)
        {
            try
            {
                var result = await _spellService.LogicDeleteAsync(id);
                if (!result) return NotFound();
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

    }
}
