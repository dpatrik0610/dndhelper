using System.Collections.Generic;

namespace dndhelper.Authorization
{
    public interface IOwnedResource
    {
        List<string> OwnerIds { get; set; }
    }
}
