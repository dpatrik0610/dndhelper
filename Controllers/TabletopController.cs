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
    /// Image upload/serving for the tabletop. Everything else on the table goes through TabletopHub.
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

        /// <summary>DM uploads a map or token image; returns the URL to put on the map/token.</summary>
        [HttpPost("{tableId}/images")]
        [RequestSizeLimit(MaxImageBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadImage(string tableId, IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose an image to upload." });
            if (file.Length > MaxImageBytes)
                return BadRequest(new { message = "Images can be at most 15 MB." });

            var caller = new TableCaller(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                User.Identity?.Name ?? "Player",
                User.IsInRole("Admin"));

            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            buffer.Position = 0;

            var url = await _service.UploadImageAsync(tableId, caller, buffer, file.FileName);
            return Ok(new { url });
        }

        /// <summary>
        /// Anonymous because SVG &lt;image&gt; can't send the bearer token. ObjectIds are not secret,
        /// so table images are effectively public to anyone with the link (same as Roll20 map art).
        /// </summary>
        [AllowAnonymous]
        [HttpGet("images/{id}")]
        public async Task<IActionResult> GetImage(string id)
        {
            var image = await _service.OpenImageAsync(id);
            if (image == null) return NotFound();

            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(image.Value.Content, image.Value.ContentType);
        }
    }
}
