using dndhelper.Models.RollModels;

using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IRollRepository : IRepository<RollRecord>
    {
        Task<List<RollRecord>> QueryAsync(FilterDefinition<RollRecord> filter, int skip, int limit);
    }
}
