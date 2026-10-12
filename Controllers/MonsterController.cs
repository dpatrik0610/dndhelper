using dndhelper.Models;
using dndhelper.Services;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    /// <summary>
    /// Campaign membership / DM rights are enforced in MonsterService; errors are mapped by ExceptionMiddleware.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MonsterController : ControllerBase
    {
        private readonly IMonsterService _monsters;

        public MonsterController(IMonsterService monsters) => _monsters = monsters;

        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] MonsterSearchCriteria criteria)
        {
            var (items, total) = await _monsters.SearchAsync(criteria);
            return Ok(new { items, total });
        }

        [HttpGet("types")]
        public async Task<ActionResult<List<string>>> Types() => await _monsters.GetTypesAsync();

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id) =>
            await _monsters.GetByIdAsync(id) is Monster m ? Ok(m) : NotFound();

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Monster monster)
        {
            monster.Id = null;
            monster.ImageUrl = null; // art only through the upload endpoint
            var created = await _monsters.CreateAsync(monster) ?? throw new System.InvalidOperationException("Could not create the monster.");
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Monster monster)
        {
            if (id != monster.Id) return BadRequest("ID mismatch.");
            if (await _monsters.GetByIdAsync(id) is not Monster existing) return NotFound();
            monster.ImageUrl = existing.ImageUrl; // art only through the upload endpoint
            return await _monsters.UpdateAsync(monster) is Monster m ? Ok(m) : NotFound();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id) =>
            await _monsters.DeleteAsync(id) ? NoContent() : NotFound();

        /// <summary>Monster art, stored in R2 under images/monsters (max 10 MB); also its tabletop token.</summary>
        [HttpPost("{id}/image")]
        [RequestSizeLimit(11 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(string id, IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose an image to upload." });

            await using var content = file.OpenReadStream();
            return Ok(await _monsters.UploadImageAsync(id, content, file.FileName));
        }

        // --- SRD (dnd5eapi.co) import into core content: superadmin only ---

        /// <summary>ruleset: 2014 (SRD 5.1) or 2024 (SRD 5.2). Flags names already in the bestiary.</summary>
        [Authorize(Roles = "Admin")]
        [HttpGet("srd/{ruleset}")]
        public async Task<ActionResult<List<SrdMonsterEntry>>> SrdCatalog(string ruleset) =>
            await _monsters.GetSrdCatalogAsync(ruleset);

        [Authorize(Roles = "Admin")]
        [HttpPost("srd/{ruleset}/import")]
        public async Task<ActionResult<SrdImportResult>> SrdImport(string ruleset, [FromBody] SrdImportRequest request) =>
            await _monsters.ImportSrdAsync(ruleset, request.Keys ?? new());
    }

    public class SrdImportRequest
    {
        public List<string>? Keys { get; set; }
    }
}
