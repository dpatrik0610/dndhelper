using dndhelper.Models.RuleModels;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [ApiController]
    [Route("api/rule-categories")]
    public class RuleCategoriesController : ControllerBase
    {
        private readonly IRuleCategoryService _service;
        private readonly ILogger _logger;

        public RuleCategoriesController(IRuleCategoryService service, ILogger logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll()
        {
            var categories = await _service.GetCategoryListAsync();
            return Ok(categories);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] RuleCategoryDto request)
        {
            try
            {
                var created = await _service.CreateCategoryAsync(request);
                if (created == null) return BadRequest("Failed to create rule category.");

                return CreatedAtAction(nameof(GetAll), new { slug = created.Slug }, created);
            }
            catch (ArgumentException ex)
            {
                _logger.Warning(ex, "Failed to create rule category");
                return BadRequest(ex.Message);
            }
        }
    }
}
