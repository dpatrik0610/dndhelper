using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    /// <summary>
    /// Image uploads for the tabletop (stored in R2). Everything else on the table goes through TabletopHub.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TabletopController : ControllerBase
    {
        private const long MaxImageBytes = 15 * 1024 * 1024;
        private readonly ITabletopService _service;

        public TabletopController(ITabletopService service)
        {
            _service = service;
        }

        /// <summary>DM uploads a map or token image (kind = "map" | "token"); returns the URL to put on the map/token.</summary>
        [HttpPost("{tableId}/images")]
        [RequestSizeLimit(MaxImageBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadImage(string tableId, IFormFile? file, [FromQuery] string kind = "map")
        {
            StorageFolder? folder = kind switch { "map" => StorageFolder.Maps, "token" => StorageFolder.Tokens, _ => null };
            if (folder == null)
                return BadRequest(new { message = "kind must be map or token." });
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose an image to upload." });
            if (file.Length > MaxImageBytes)
                return BadRequest(new { message = "Images can be at most 15 MB." });

            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            buffer.Position = 0;

            var url = await _service.UploadImageAsync(tableId, Caller, buffer, file.FileName, folder.Value);
            return Ok(new { url });
        }

        /// <summary>The caller's uploaded maps, to reuse or delete (frees a slot of the per-DM limit).</summary>
        [HttpGet("maps")]
        public async Task<IActionResult> ListMaps() => Ok(await _service.ListMapsAsync(Caller));

        [HttpDelete("maps/{fileId}")]
        public async Task<IActionResult> DeleteMap(string fileId)
        {
            await _service.DeleteMapAsync(Caller, fileId);
            return NoContent();
        }

        private TableCaller Caller => new(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            User.Identity?.Name ?? "Player",
            User.IsInRole("Admin"));
    }
}
