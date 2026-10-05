using dndhelper.Database;
using dndhelper.Database.Migrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/database")]
    public class DatabaseController : ControllerBase
    {
        private readonly MongoDbContext _context;
        private readonly ILogger _logger;

        public DatabaseController(MongoDbContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// One-shot move to campaign-scoped data. Defaults to a dry run that only reports counts.
        /// Download a backup first. Safe to re-run: a second run reports zero changes.
        /// </summary>
        [HttpPost("migrate/campaign-scope")]
        public async Task<ActionResult<Dictionary<string, long>>> MigrateCampaignScope(
            [FromQuery] string targetCampaignId,
            [FromServices] CampaignScopeMigration migration,
            [FromServices] dndhelper.Services.Interfaces.ICacheService cache,
            [FromQuery] bool dryRun = true)
        {
            var report = await migration.RunAsync(targetCampaignId, dryRun);
            if (!dryRun)
                cache.ClearAllFromCache(); // repositories cache entities without the new fields
            return Ok(report);
        }

        [HttpGet("collections")]
        public async Task<ActionResult<List<string>>> GetCollections(CancellationToken cancellationToken)
        {
            try
            {
                var names = await _context.ListCollectionsAsync(cancellationToken);
                return Ok(names);
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                _logger.Error(ex, "Failed to list MongoDB collections for {DbName}", _context.DatabaseName);
                return StatusCode(500, "Failed to list database collections.");
            }
        }
    }
}
