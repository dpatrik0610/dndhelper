using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    /// <summary>
    /// Core content = campaign content with CampaignId null: read-only, shared with every
    /// campaign that imports its type. Only the superadmin moves things into core.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/core")]
    public class CoreController : ControllerBase
    {
        private readonly ISpellRepository _spells;
        private readonly IEquipmentRepository _equipment;
        private readonly IMonsterRepository _monsters;
        private readonly IRuleRepository _rules;
        private readonly ICampaignRepository _campaigns;

        public CoreController(ISpellRepository spells, IEquipmentRepository equipment, IMonsterRepository monsters,
            IRuleRepository rules, ICampaignRepository campaigns)
        {
            _spells = spells;
            _equipment = equipment;
            _monsters = monsters;
            _rules = rules;
            _campaigns = campaigns;
        }

        /// <summary>type: Spells | Equipment | Monsters | Rules (case-insensitive).</summary>
        [HttpPost("{type}/{id}/promote")]
        public async Task<IActionResult> Promote(string type, string id)
        {
            string? sourceCampaignId;
            string coreType;

            switch (type.ToLowerInvariant())
            {
                case "spells":
                    sourceCampaignId = (await _spells.GetByIdAsync(id) ?? throw Missing(id)).CampaignId;
                    await _spells.SetCampaignAsync(id, null);
                    coreType = CoreContentTypes.Spells;
                    break;
                case "equipment":
                    sourceCampaignId = (await _equipment.GetByIdAsync(id) ?? throw Missing(id)).CampaignId;
                    await _equipment.SetCampaignAsync(id, null);
                    coreType = CoreContentTypes.Equipment;
                    break;
                case "monsters":
                    sourceCampaignId = (await _monsters.GetByIdAsync(id) ?? throw Missing(id)).CampaignId;
                    await _monsters.SetCampaignAsync(id, null);
                    coreType = CoreContentTypes.Monsters;
                    break;
                case "rules":
                    var rule = await _rules.GetByIdAsync(id) ?? throw Missing(id);
                    if (await _rules.SlugExistsAsync(rule.Slug, null, rule.Id))
                        return Conflict(new { message = $"A core rule with slug '{rule.Slug}' already exists." });
                    sourceCampaignId = rule.CampaignId;
                    await _rules.SetCampaignAsync(id, null);
                    coreType = CoreContentTypes.Rules;
                    break;
                default:
                    return BadRequest(new { message = $"Unknown core type '{type}'." });
            }

            // The source campaign keeps seeing what it just promoted.
            if (sourceCampaignId != null && await _campaigns.GetByIdAsync(sourceCampaignId) is { } campaign
                && !campaign.CoreImports.Contains(coreType))
            {
                campaign.CoreImports.Add(coreType);
                await _campaigns.UpdateAsync(campaign);
            }

            return NoContent();
        }

        /// <summary>
        /// The reverse of promote: hands a core (or misplaced) spell/item/monster/rule to one campaign.
        /// type: Spells | Equipment | Monsters | Rules (case-insensitive).
        /// </summary>
        [HttpPost("{type}/{id}/move-to/{campaignId}")]
        public async Task<IActionResult> MoveToCampaign(string type, string id, string campaignId)
        {
            if (await _campaigns.GetByIdAsync(campaignId) == null)
                return NotFound(new { message = "Campaign not found." });

            bool moved;
            switch (type.ToLowerInvariant())
            {
                case "spells": moved = await _spells.SetCampaignAsync(id, campaignId); break;
                case "equipment": moved = await _equipment.SetCampaignAsync(id, campaignId); break;
                case "monsters": moved = await _monsters.SetCampaignAsync(id, campaignId); break;
                case "rules":
                    var rule = await _rules.GetByIdAsync(id) ?? throw Missing(id);
                    if (await _rules.SlugExistsAsync(rule.Slug, campaignId, rule.Id))
                        return Conflict(new { message = $"That campaign already has a rule with slug '{rule.Slug}'." });
                    moved = await _rules.SetCampaignAsync(id, campaignId);
                    break;
                default:
                    return BadRequest(new { message = $"Unknown core type '{type}'." });
            }
            return moved ? NoContent() : NotFound(new { message = $"Nothing found with id {id}." });
        }

        private static NotFoundException Missing(string id) => new($"Nothing found with id {id}.");
    }
}
