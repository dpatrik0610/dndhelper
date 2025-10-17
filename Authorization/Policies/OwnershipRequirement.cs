using Microsoft.AspNetCore.Authorization;

namespace dndhelper.Authorization.Policies
{
    public class OwnershipRequirement : IAuthorizationRequirement
    {
        public OwnershipRequirement() { }
    }
}
