using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class NoteController : ControllerBase
    {
        private readonly INoteService _noteService;
        private readonly ILogger _logger;

        public NoteController(INoteService noteService, ILogger logger)
        {
            _noteService = noteService;
            _logger = logger;
        }

        // GET: api/note/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Note ID is required." });

            try
            {
                var note = await _noteService.GetByIdAsync(id);
                return Ok(new { data = note, message = "Note retrieved successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving note {Id}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // GET: api/note/many?ids=1,2,3
        [HttpGet("many")]
        public async Task<IActionResult> GetMany([FromQuery] string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return BadRequest(new { message = "No note IDs provided." });

            try
            {
                var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                var notes = await _noteService.GetByIdsAsync(idList);

                return Ok(new { data = notes, message = "Notes retrieved successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving note list {Ids}", ids);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // POST: api/note
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Note note)
        {
            if (note == null)
                return BadRequest(new { message = "Invalid note payload." });

            try
            {
                note.CreatedAt = DateTime.UtcNow;
                note.UpdatedAt = DateTime.UtcNow;

                var created = await _noteService.CreateAsync(note);
                return Ok(new { data = created, message = "Note created successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating note");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // PUT: api/note/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Note updatedNote)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Note ID is required." });

            if (updatedNote == null)
                return BadRequest(new { message = "Invalid note payload." });

            try
            {
                updatedNote.Id = id;
                updatedNote.UpdatedAt = DateTime.UtcNow;

                var saved = await _noteService.UpdateAsync(updatedNote);
                return Ok(new { data = saved, message = "Note updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating note {Id}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // DELETE: api/note/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Note ID is required." });

            try
            {
                await _noteService.DeleteAsync(id);
                return Ok(new { message = "Note deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting note {Id}", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
