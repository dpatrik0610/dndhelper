using dndhelper.Database;
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

        [HttpGet("collections")]
        public async Task<ActionResult<List<string>>> GetCollections(CancellationToken cancellationToken)
        {
            try
            {
                var names = await _context.ListCollectionsAsync(cancellationToken);
                return Ok(names);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to list MongoDB collections for {DbName}", _context.DatabaseName);
                return StatusCode(500, "Failed to list database collections.");
            }
        }
    }
}
