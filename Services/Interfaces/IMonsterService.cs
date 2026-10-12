using dndhelper.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IMonsterService : IBaseService<Monster>, IInternalBaseService<Monster>
    {
        Task<(List<Monster> Items, long Total)> SearchAsync(MonsterSearchCriteria criteria);
        Task<List<string>> GetTypesAsync();
        Task<Monster> UploadImageAsync(string monsterId, Stream content, string fileName);

        Task<List<SrdMonsterEntry>> GetSrdCatalogAsync(string ruleset);
        Task<SrdImportResult> ImportSrdAsync(string ruleset, List<string> keys);
    }
}
