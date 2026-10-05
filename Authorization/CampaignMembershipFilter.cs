using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace dndhelper.Authorization
{
    /// <summary>
    /// Global guard: a route with a {campaignId} value is rejected unless the caller is a member of that
    /// campaign. The X-Campaign-Id header is deliberately not checked here (list/create paths validate it),
    /// so a stale header can't lock a user out of listing or joining campaigns.
    /// </summary>
    public class CampaignMembershipFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.HttpContext.User.Identity?.IsAuthenticated == true
                && context.RouteData.Values.TryGetValue("campaignId", out var value)
                && value?.ToString() is { Length: > 0 } campaignId)
            {
                var access = context.HttpContext.RequestServices.GetRequiredService<CampaignAccess>();
                await access.EnsureMemberAsync(campaignId);
            }

            await next();
        }
    }
}
