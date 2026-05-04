using dndhelper.Models.EncounterRoomModels;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    /// <summary>
    /// REST endpoints for EncounterRoom lifecycle operations.
    /// All real-time mutations (move token, update entity, draw, advance turn)
    /// go through SignalR only — this controller handles room CRUD + initial state retrieval.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class EncounterRoomController : ControllerBase
    {
        private readonly IEncounterRoomService _roomService;
        private readonly ILogger _logger;

        public EncounterRoomController(IEncounterRoomService roomService, ILogger logger)
        {
            _roomService = roomService;
            _logger = logger;
        }

        /// <summary>
        /// Create a new encounter room. The caller becomes the DM.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateRoomRequest request)
        {
            try
            {
                Guard.NotNull(request, nameof(request));
                Guard.NotNullOrWhiteSpace(request.Name, nameof(request.Name));

                var room = await _roomService.CreateRoomAsync(request.Name, request.MapSettings);
                return CreatedAtAction(nameof(GetById), new { id = room.Id }, room);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Get room state by ID. Requires room membership.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(id, nameof(id));
                var room = await _roomService.GetRoomAsync(id);
                if (room == null) return NotFound(new { message = "Room not found." });
                return Ok(room);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Get all rooms the current user is a member of (as DM or player).
        /// </summary>
        [HttpGet("my")]
        public async Task<IActionResult> GetMyRooms()
        {
            var rooms = await _roomService.GetMyRoomsAsync();
            return Ok(rooms);
        }

        /// <summary>
        /// Join a room via a join code. Returns the full room state.
        /// </summary>
        [HttpPost("join")]
        public async Task<IActionResult> Join([FromBody] JoinRoomRequest request)
        {
            try
            {
                Guard.NotNull(request, nameof(request));
                Guard.NotNullOrWhiteSpace(request.JoinCode, nameof(request.JoinCode));

                var response = await _roomService.JoinRoomAsync(request.JoinCode);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Leave a room. DMs cannot leave — they must end the room instead.
        /// </summary>
        [HttpPost("{id}/leave")]
        public async Task<IActionResult> Leave(string id)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(id, nameof(id));
                await _roomService.LeaveRoomAsync(id);
                return Ok(new { message = "Left the room." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Soft-delete a room. DM only.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> End(string id)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(id, nameof(id));
                await _roomService.EndRoomAsync(id);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Regenerate the join code for a room. DM only.
        /// </summary>
        [HttpPost("{id}/code")]
        public async Task<IActionResult> RegenerateCode(string id)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(id, nameof(id));
                var newCode = await _roomService.RegenerateJoinCodeAsync(id);
                return Ok(new { joinCode = newCode });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Send invite notifications to campaign members. DM only.
        /// </summary>
        [HttpPost("{id}/invite")]
        public async Task<IActionResult> InvitePlayers(string id, [FromBody] InvitePlayersRequest request)
        {
            try
            {
                Guard.NotNullOrWhiteSpace(id, nameof(id));
                Guard.NotNull(request, nameof(request));

                await _roomService.InvitePlayersAsync(id, request.UserIds);
                return Ok(new { message = $"Invited {request.UserIds.Count} players." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
