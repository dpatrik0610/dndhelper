using dndhelper.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IPublicDndApiClient
    {
        Task<IEnumerable<Equipment>> GetEquipmentListAsync();
        Task<Equipment?> GetEquipmentByIndexAsync(string index);

        /// <summary>SRD monster list for a ruleset ("2014" or "2024").</summary>
        Task<List<(string Index, string Name)>> GetMonsterIndexAsync(string ruleset);
        Task<JObject?> GetMonsterAsync(string ruleset, string index);
        /// <summary>An image path from the API ("/api/images/monsters/x.png"); null when it isn't there.</summary>
        Task<byte[]?> GetImageAsync(string path);
    }
}
