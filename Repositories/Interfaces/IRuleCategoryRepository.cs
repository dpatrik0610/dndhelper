using dndhelper.Models.RuleModels;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IRuleCategoryRepository : IRepository<RuleCategory>
    {
        Task<RuleCategory?> GetBySlugAsync(string slug);
        Task<bool> SlugExistsAsync(string slug, string? excludeId = null);
    }
}
