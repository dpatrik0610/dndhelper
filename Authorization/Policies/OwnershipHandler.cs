using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Authorization.Policies
{
    public class OwnershipHandler : AuthorizationHandler<OwnershipRequirement, IOwnedResource>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            OwnershipRequirement requirement,
            IOwnedResource resource)
        {
            // resource.OwnerId is something your entity exposes
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (context.User.IsInRole("Admin") || (userId != null && resource.OwnerIds.Contains(userId)))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
