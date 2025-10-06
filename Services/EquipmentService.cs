using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Serilog;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class EquipmentService : BaseService<Equipment, IEquipmentRepository>, IEquipmentService
    {
        private readonly IPublicDndApiClient _apiClient;  // Wrapper for Official DnD API calls.

        public EquipmentService(IEquipmentRepository repository, IPublicDndApiClient apiClient, ILogger logger): base(repository, logger)
        {
            _apiClient = apiClient;
        }

        public async Task<Equipment?> GetEquipmentByIndexAsync(string index)
        {
            var local = await _repository.GetByIndexAsync(index);
            if (local != null) return local;

            //var official = await _apiClient.GetEquipmentByIndexAsync(index);
            //if (official == null) return official;

            return null;
        }

        public async Task DeleteByIndexAsync(string index)
        {
            await _repository.DeleteByIndex(index);
        }

        public async Task<bool> CheckIfIndexExists(string index)
        {
            var localExists = await _repository.GetByIndexAsync(index) != null;
            if (localExists) return true;

            return false;
        }
    }
}
