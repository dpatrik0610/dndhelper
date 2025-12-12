using dndhelper.Models.RuleModels;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IRuleService : IBaseService<Rule>
    {
        Task<RuleListResponse> GetListAsync(RuleQueryOptions options);
        Task<RuleDetailDto?> GetBySlugAsync(string slug);
        Task<RuleDetailResponse?> GetDetailAsync(string slug);
        Task<RuleStats> GetStatsAsync();
        Task<RuleDetailDto?> CreateRuleAsync(RuleDetailDto dto);
        Task<RuleDetailDto?> UpdateRuleAsync(string slug, RuleDetailDto dto);
    }
}
