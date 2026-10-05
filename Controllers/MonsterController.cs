using dndhelper.Models;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    // Campaign membership / DM rights are enforced in MonsterService.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MonsterController : ControllerBase
    {
        private readonly IMonsterService _monsterService;
        private readonly ILogger _logger;

        public MonsterController(IMonsterService monsterService, ILogger logger)
        {
            _monsterService = monsterService;
            _logger = logger;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var monster = await _monsterService.GetByIdAsync(id);
                if (monster == null) return NotFound();
                return Ok(monster);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("name/{name}")]
        public async Task<IActionResult> GetByName(string name)
        {
            try
            {
                var monster = await _monsterService.GetMonstersByNameAsync(name);

                if (monster.IsNullOrEmpty()) return NotFound();

                return Ok(monster);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var monsters = await _monsterService.GetAllAsync();
            return Ok(monsters);
        }

        [HttpGet("paged")]
        public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var monsters = await _monsterService.GetPagedMonstersAsync(page, pageSize);
                return Ok(monsters);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] string? name,
            [FromQuery] string? type,
            [FromQuery] double? minCR,
            [FromQuery] double? maxCR,
            [FromQuery] List<string>? tags,
            [FromQuery] string? sortBy = "Name",
            [FromQuery] bool desc = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var monsters = await _monsterService.AdvancedSearchAsync(new MonsterSearchCriteria
                {
                    Name = name,
                    Type = type,
                    MinCR = minCR,
                    MaxCR = maxCR,
                    Tags = tags,
                    SortBy = sortBy!,
                    SortDescending = desc,
                    Page = page,
                    PageSize = pageSize
                });

                return Ok(new { Found = monsters.Count, monsters });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }


        // --- Admin only ---

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Monster monster)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User ID claim missing.");

                monster.CreatedByUserId = userId;
                monster.OwnerIds!.Add(userId);

                var created = await _monsterService.CreateAsync(monster);
                return CreatedAtAction(nameof(GetById), new { id = created!.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }


        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Monster monster)
        {
            if (id != monster.Id)
                return BadRequest("ID mismatch.");

            try
            {
                var updated = await _monsterService.UpdateAsync(monster);
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
                await _monsterService.DeleteAsync(id);
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
                var result = await _monsterService.LogicDeleteAsync(id);
                if (!result) return NotFound();
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("delete-own/{id}")]
        public async Task<IActionResult> DeleteOwn(string id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User ID claim missing.");

                var result = await _monsterService.DeleteOwnMonsterAsync(id, userId);
                if (!result) return NotFound();

                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{monsterId}/switch-owner/{id}")]
        public async Task<IActionResult> SwitchOwner(string monsterId, string id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User ID claim missing.");

                var result = await _monsterService.SwitchMonsterOwnerAsync(monsterId, id, userId);
                if (!result) return NotFound();

                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{monsterId}/addOwner/{newOwner}")]
        public async Task<IActionResult> AddOwner(string monsterId, string newOwner)
        {
            try
            {
                if (string.IsNullOrEmpty(monsterId) || string.IsNullOrEmpty(newOwner))
                    return BadRequest("Monster ID and User ID cannot be null or empty.");

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var result = await _monsterService.AddMonsterOwnerAsync(monsterId, newOwner, userId!);
                if (!result) return NotFound();
                _logger.Information($"Added new owner {newOwner} to monster {monsterId} by user {userId}");
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("count")]
        public async Task<IActionResult> GetCount()
        {
            try
            {
                var count = await _monsterService.GetCountAsync();
                return Ok(new { Count = count });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

    }
}
