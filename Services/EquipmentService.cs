using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class EquipmentService : IEquipmentService
    {
        private readonly IEquipmentRepository _repo;
        private readonly IPublicDndApiClient _apiClient;  // Wrapper for Official DnD API calls.

        public EquipmentService(IEquipmentRepository repo, IPublicDndApiClient apiClient)
        {
            _repo = repo;
            _apiClient = apiClient;
        }

        public async Task<IEnumerable<Equipment>> GetAllEquipmentAsync()
        {
            var equipments = await _repo.GetEquipmentAsync() ?? new List<Equipment>();

            return equipments;
        }

        public async Task<Equipment?> GetEquipmentByIndexAsync(string index)
        {
            var local = await _repo.GetEquipmentByIndexAsync(index);
            if (local != null) return local;

            //var official = await _apiClient.GetEquipmentByIndexAsync(index);
            //if (official == null) return official;

            return null;
        }

        public async Task<Equipment> CreateEquipmentAsync(Equipment equipment)
        {
            equipment.IsCustom = true;
            if (await CheckIfIndexExists(equipment.Index)) 
                throw new Exception("Index already Exists.");

            return await _repo.AddEquipmentAsync(equipment);
        }

        public async Task<Equipment> UpdateEquipmentAsync(Equipment equipment)
        {
            equipment.IsCustom = true;
            return await _repo.UpdateEquipmentAsync(equipment);
        }

        public async Task DeleteEquipmentAsync(string index)
        {
            await _repo.DeleteEquipmentAsync(index);
        }

        public async Task<bool> CheckIfIndexExists(string index)
        {
            // Check local DB first
            var localExists = await _repo.GetEquipmentByIndexAsync(index) != null;
            if (localExists) return true;

            return false;
            //// If not local, check public API
            //var official = await _apiClient.GetEquipmentByIndexAsync(index);
            //return official != null;
        }
    }
}
