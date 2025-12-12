using dndhelper.Models.RuleModels;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IRuleRepository : IRepository<Rule>
    {
        Task<Rule?> GetBySlugAsync(string slug);
        Task<RuleQueryResult> QueryAsync(RuleQueryOptions options);
        Task<RuleStats> GetStatsAsync();
        Task<bool> SlugExistsAsync(string slug, string? excludeId = null);
    }
}
