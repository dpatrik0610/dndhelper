using dndhelper.Services;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Linq;

namespace dndhelper.Controllers
{
    [ApiController]
    [Route("api/cache")]
    public class CacheController : ControllerBase
    {
        private readonly ICacheService _cacheService;
        private readonly ILogger _logger;

        public CacheController(ICacheService cacheService, ILogger logger)
        {
            _cacheService = cacheService;
            _logger = logger;
        }

        // GET: api/cache/info
        [HttpGet("info")]
        [Authorize(Roles = "Admin")]
        public IActionResult Info()
        {
            var keys = _cacheService.GetAllKeys();

            var grouped = keys
                .GroupBy(k => k.Split('_')[0])
                .ToDictionary(g => g.Key, g => g.ToList());

            return Ok(new { collections = grouped, total = keys.Count });
        }

        // DELETE: api/cache
        [HttpDelete]
        [Authorize(Roles = "Admin")]
        public IActionResult ClearAll()
        {
            var keys = _cacheService.GetAllKeys();

            // Clear items from actual IMemoryCache
            _cacheService.ClearAllFromCache();

            // Clear tracked key index
            _cacheService.ClearAllTracked();

            return Ok(new { removed = keys, count = keys.Count });
        }
    }
}
