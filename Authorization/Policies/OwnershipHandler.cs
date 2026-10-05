using dndhelper.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Authorization.Policies
{
    /// <summary>
    /// Passes for the superadmin, an owner of the resource, or a DM of the campaign the resource belongs to.
    /// Scoped (not singleton) because it needs the per-request CampaignAccess.
    /// </summary>
    public class OwnershipHandler : AuthorizationHandler<OwnershipRequirement, IOwnedResource>
    {
        private readonly CampaignAccess _campaignAccess;

        public OwnershipHandler(CampaignAccess campaignAccess)
        {
            _campaignAccess = campaignAccess;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            OwnershipRequirement requirement,
            IOwnedResource resource)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (context.User.IsInRole(CampaignAccess.SuperAdminRole)
                || (userId != null && resource.OwnerIds?.Contains(userId) == true)
                || (resource is ICampaignScoped { CampaignId: not null } scoped && await _campaignAccess.IsDmAsync(scoped.CampaignId)))
            {
                context.Succeed(requirement);
            }
        }
    }
}
