using System.Collections.Generic;

namespace dndhelper.Models.CharacterModels
{
    public class BulkLongRestResult
    {
        public IEnumerable<string> SuccessfulIds { get; set; }
        public IEnumerable<string> FailedIds { get; set; }
    }
}
