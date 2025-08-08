using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EquipmentController : ControllerBase
    {
        private readonly IEquipmentService _service;

        public EquipmentController(IEquipmentService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Equipment>>> GetAll()
        {
            try
            {
                var equipment = await _service.GetAllEquipmentAsync();
                return Ok(equipment);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{index}")]
        public async Task<ActionResult<Equipment>> GetByIndex(string index)
        {
            try
            {
                var equipment = await _service.GetEquipmentByIndexAsync(index);
                if (equipment == null) return NotFound();
                return Ok(equipment);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<Equipment>> Create(Equipment equipment)
        {
            try
            {
                var created = await _service.CreateEquipmentAsync(equipment);
                return CreatedAtAction(nameof(GetByIndex), new { index = created.Index }, created);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("{index}")]
        public async Task<ActionResult<Equipment>> Update(string index, Equipment equipment)
        {
            try
            {
                if (index != equipment.Index)
                    return BadRequest("Index mismatch.");

                var updated = await _service.UpdateEquipmentAsync(equipment);
                return Ok(updated);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("{index}")]
        public async Task<IActionResult> Delete(string index)
        {
            try
            {
                await _service.DeleteEquipmentAsync(index);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
