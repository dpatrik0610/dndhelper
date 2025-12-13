using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class BackupController : ControllerBase
    {
        private readonly IBackupService _backupService;
        private readonly ILogger _logger;

        public BackupController(IBackupService backupService, ILogger logger)
        {
            _backupService = backupService;
            _logger = logger;
        }

        [HttpGet("{collectionName}")]
        public async Task<IActionResult> ExportCollection(string collectionName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                return BadRequest(new { message = "Collection name is required." });

            try
            {
                var result = await _backupService.ExportCollectionAsync(collectionName, cancellationToken);
                return File(result.Stream, result.ContentType, result.FileName);
            }
            catch (ArgumentException ex)
            {
                _logger.Warning(ex, "Backup failed for collection {Collection}", collectionName);
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting collection {Collection}", collectionName);
                return StatusCode(500, new { message = "Failed to export collection." });
            }
        }

        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ExportAllCollections(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _backupService.ExportAllCollectionsAsync(cancellationToken);
                return File(result.Stream, result.ContentType, result.FileName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting all collections");
                return StatusCode(500, new { message = "Failed to export all collections." });
            }
        }

        [HttpPost("{collectionName}/restore")]
        public async Task<IActionResult> RestoreCollection(string collectionName, IFormFile? file, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                return BadRequest(new { message = "Collection name is required." });

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "A gzip archive file is required." });

            try
            {
                await using var stream = new MemoryStream();
                await file.CopyToAsync(stream, cancellationToken);
                stream.Position = 0;

                await _backupService.RestoreCollectionAsync(collectionName, stream, cancellationToken);
                return Ok(new { message = $"Collection '{collectionName}' restored." });
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warning(ex, "Restore failed for collection {Collection}", collectionName);
                return StatusCode(500, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error restoring collection {Collection}", collectionName);
                return StatusCode(500, new { message = "Failed to restore collection." });
            }
        }
    }
}
