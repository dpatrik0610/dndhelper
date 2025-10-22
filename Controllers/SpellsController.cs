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

        // --- READ endpoints: accessible by User and Admin ---

        [HttpGet("{id}")]
        [Authorize(Roles = "User,Admin")]
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

        //[HttpGet("name/{name}")]
        //[Authorize(Roles = "User,Admin")]
        //public async Task<IActionResult> GetByName(string name)
        //{
        //    try
        //    {
        //        var spell = await _spellService.GetspellsByNameAsync(name);

        //        if (spell.IsNullOrEmpty()) return NotFound();

        //        return Ok(spell);
        //    }
        //    catch (ArgumentException ex)
        //    {
        //        return BadRequest(ex.Message);
        //    }
        //}

        [HttpGet]
        [Authorize(Roles = "User,Admin")]
        public async Task<IActionResult> GetAll()
        {
            var spells = await _spellService.GetAllAsync();
            return Ok(spells);
        }

        //[HttpGet("paged")]
        //[Authorize(Roles = "User,Admin")]
        //public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        //{
        //    try
        //    {
        //        var spells = await _spellService.GetPagedspellsAsync(page, pageSize);
        //        return Ok(spells);
        //    }
        //    catch (ArgumentException ex)
        //    {
        //        return BadRequest(ex.Message);
        //    }
        //}

        //[HttpGet("search")]
        //[Authorize(Roles = "User,Admin,DungeonMaster")]
        //public async Task<IActionResult> Search(
        //    [FromQuery] string? name,
        //    [FromQuery] string? type,
        //    [FromQuery] double? minCR,
        //    [FromQuery] double? maxCR,
        //    [FromQuery] List<string>? tags,
        //    [FromQuery] string? sortBy = "Name",
        //    [FromQuery] bool desc = false,
        //    [FromQuery] int page = 1,
        //    [FromQuery] int pageSize = 10)
        //{
        //    try
        //    {
        //        var spells = await _spellService.AdvancedSearchAsync(new spellSearchCriteria
        //        {
        //            Name = name,
        //            Type = type,
        //            MinCR = minCR,
        //            MaxCR = maxCR,
        //            Tags = tags,
        //            SortBy = sortBy!,
        //            SortDescending = desc,
        //            Page = page,
        //            PageSize = pageSize
        //        });

        //        return Ok(new { Found = spells.Count, spells });
        //    }
        //    catch (ArgumentException ex)
        //    {
        //        return BadRequest(ex.Message);
        //    }
        //}


        // --- Admin only ---

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] Spell spell)
        {
            try
            {
                // Extract user ID from claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User ID claim missing.");

                // Optionally assign creator ID to spell or log it.
                //spell.CreatedByUserId = userId;
                //spell.OwnerIds!.Add(userId);

                var created = await _spellService.CreateAsync(spell);
                return CreatedAtAction(nameof(GetById), new { id = created!.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }


        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
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
        [Authorize(Roles = "Admin")]
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
        [Authorize(Roles = "Admin")]
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
