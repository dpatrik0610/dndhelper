using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    /// <summary>Errors (not found, forbidden, validation) are mapped by ExceptionMiddleware.</summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChronicleController : ControllerBase
    {
        private const int MaxImageBytes = 3 * 1024 * 1024;

        private readonly IChronicleService _chronicle;

        public ChronicleController(IChronicleService chronicle)
        {
            _chronicle = chronicle;
        }

        [HttpGet("campaign/{campaignId}")]
        public async Task<IActionResult> GetByCampaign(string campaignId) => Ok(await _chronicle.GetEntriesAsync(campaignId));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id) => Ok(await _chronicle.GetEntryAsync(id));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ChronicleEntryRequest request) => Ok(await _chronicle.CreateEntryAsync(request));

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] ChronicleEntryRequest request) =>
            Ok(await _chronicle.UpdateEntryAsync(id, request));

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id) =>
            await _chronicle.DeleteEntryAsync(id) ? NoContent() : NotFound(new { message = "Entry not found." });

        /// <summary>An image for an entry's markdown; stored on the CDN under images/chronicle, max 3 MB.</summary>
        [HttpPost("campaign/{campaignId}/images")]
        [RequestSizeLimit(MaxImageBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadImage(string campaignId, IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose an image to upload." });

            await using var content = file.OpenReadStream();
            return Ok(new ChronicleImageResult { Url = await _chronicle.UploadImageAsync(campaignId, content, file.FileName) });
        }

        [HttpGet("campaign/{campaignId}/folders")]
        public async Task<IActionResult> GetFolders(string campaignId) => Ok(await _chronicle.GetFoldersAsync(campaignId));

        [HttpPost("folders")]
        public async Task<IActionResult> CreateFolder([FromBody] ChronicleFolderRequest request) => Ok(await _chronicle.CreateFolderAsync(request));

        [HttpPut("folders/{id}")]
        public async Task<IActionResult> UpdateFolder(string id, [FromBody] ChronicleFolderRequest request) =>
            Ok(await _chronicle.UpdateFolderAsync(id, request));

        [HttpDelete("folders/{id}")]
        public async Task<IActionResult> DeleteFolder(string id) =>
            await _chronicle.DeleteFolderAsync(id) ? NoContent() : NotFound(new { message = "Folder not found." });

        [HttpGet("{id}/comments")]
        public async Task<IActionResult> GetComments(string id) => Ok(await _chronicle.GetCommentsAsync(id));

        [HttpPost("{id}/comments")]
        public async Task<IActionResult> AddComment(string id, [FromBody] ChronicleCommentRequest request) =>
            Ok(await _chronicle.AddCommentAsync(id, request));

        [HttpDelete("{id}/comments/{commentId}")]
        public async Task<IActionResult> DeleteComment(string id, string commentId) =>
            await _chronicle.DeleteCommentAsync(id, commentId) ? NoContent() : NotFound(new { message = "Comment not found." });
    }
}
