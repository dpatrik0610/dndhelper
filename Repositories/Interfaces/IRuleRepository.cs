using dndhelper.Models.RuleModels;
using MongoDB.Driver;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IRuleRepository : IRepository<Rule>
    {
        Task<Rule?> GetBySlugAsync(string slug, FilterDefinition<Rule> scope);
        Task<RuleQueryResult> QueryAsync(RuleQueryOptions options, FilterDefinition<Rule> scope);
        Task<RuleStats> GetStatsAsync(FilterDefinition<Rule> scope);
        Task<bool> SlugExistsAsync(string slug, string? campaignId, string? excludeId = null);
    }
}
