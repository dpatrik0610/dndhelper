using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SessionController : ControllerBase
    {
        private readonly ISessionService _sessions;

        public SessionController(ISessionService sessions)
        {
            _sessions = sessions;
        }

        [HttpGet("campaign/{campaignId}")]
        public async Task<IActionResult> GetByCampaign(string campaignId) =>
            Ok(await _sessions.GetByCampaignIdAsync(campaignId));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id) =>
            Ok(await _sessions.GetVisibleAsync(id));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Session session)
        {
            var created = await _sessions.CreateAndNotifyAsync(session);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Session session) =>
            Ok(await _sessions.UpdateDetailsAsync(id, session));

        [HttpPost("{id}/start")]
        public async Task<IActionResult> Start(string id) =>
            Ok(await _sessions.StartAsync(id));

        [HttpPost("{id}/end")]
        public async Task<IActionResult> End(string id) =>
            Ok(await _sessions.EndAsync(id));

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id) =>
            await _sessions.DeleteAndNotifyAsync(id) ? NoContent() : NotFound(new { message = "Session not found." });

        [HttpPost("{id}/notes")]
        public async Task<IActionResult> AddNote(string id, [FromBody] SessionNoteInput note) =>
            Ok(await _sessions.AddNoteAsync(id, note));

        [HttpPut("{id}/notes/{noteId}")]
        public async Task<IActionResult> UpdateNote(string id, string noteId, [FromBody] SessionNoteInput note) =>
            Ok(await _sessions.UpdateNoteAsync(id, noteId, note));

        [HttpDelete("{id}/notes/{noteId}")]
        public async Task<IActionResult> DeleteNote(string id, string noteId) =>
            Ok(await _sessions.DeleteNoteAsync(id, noteId));
    }
}
