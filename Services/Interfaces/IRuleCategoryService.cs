using dndhelper.Models.RuleModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IRuleCategoryService : IBaseService<RuleCategory>
    {
        Task<List<RuleCategoryDto>> GetCategoryListAsync();
        Task<RuleCategoryDto?> CreateCategoryAsync(RuleCategoryDto dto);
    }
}
