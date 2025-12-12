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
    [Route("api/rules")]
    public class RulesController : ControllerBase
    {
        private readonly IRuleService _ruleService;
        private readonly ILogger _logger;

        public RulesController(IRuleService ruleService, ILogger logger)
        {
            _ruleService = ruleService;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<RuleListResponse>> GetRules(
            [FromQuery] string? category,
            [FromQuery] string? tag,
            [FromQuery] string? source,
            [FromQuery] string? search,
            [FromQuery] string? cursor,
            [FromQuery] int limit = 20)
        {
            try
            {
                var options = new RuleQueryOptions
                {
                    Category = category,
                    Tag = tag,
                    Source = source,
                    Search = search,
                    Cursor = cursor,
                    Limit = limit
                };

                var response = await _ruleService.GetListAsync(options);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                _logger.Warning(ex, "Invalid rule query parameters");
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{slug}")]
        [AllowAnonymous]
        public async Task<ActionResult<RuleDetailResponse>> GetRuleBySlug(string slug)
        {
            try
            {
                var rule = await _ruleService.GetDetailAsync(slug);
                if (rule == null) return NotFound();

                return Ok(rule);
            }
            catch (ArgumentException ex)
            {
                _logger.Warning(ex, "Invalid slug for rule detail");
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("stats")]
        [AllowAnonymous]
        public async Task<ActionResult<RuleStats>> GetStats()
        {
            var stats = await _ruleService.GetStatsAsync();
            return Ok(stats);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<RuleDetailResponse>> Create([FromBody] RuleDetailDto request)
        {
            try
            {
                var created = await _ruleService.CreateRuleAsync(request);
                if (created == null) return BadRequest("Failed to create rule.");

                return CreatedAtAction(nameof(GetRuleBySlug), new { slug = created.Slug }, new RuleDetailResponse { Rule = created });
            }
            catch (ArgumentException ex)
            {
                _logger.Warning(ex, "Failed to create rule");
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{slug}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<RuleDetailResponse>> Update(string slug, [FromBody] RuleDetailDto request)
        {
            try
            {
                var updated = await _ruleService.UpdateRuleAsync(slug, request);
                if (updated == null) return NotFound();

                return Ok(new RuleDetailResponse { Rule = updated });
            }
            catch (ArgumentException ex)
            {
                _logger.Warning(ex, "Failed to update rule {Slug}", slug);
                return BadRequest(ex.Message);
            }
        }
    }
}
