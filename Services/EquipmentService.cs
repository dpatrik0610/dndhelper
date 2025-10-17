using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class EquipmentService : BaseService<Equipment, IEquipmentRepository>, IEquipmentService
    {
        private readonly IPublicDndApiClient _apiClient;  // Wrapper for Official DnD API calls.

        public EquipmentService(IEquipmentRepository repository, IPublicDndApiClient apiClient, ILogger logger, IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor) : base(repository, logger, authorizationService, httpContextAccessor)
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
        public async Task<List<Equipment>> SearchByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Search term cannot be empty.", nameof(name));

            _logger.Information($"Searching equipments by name: {name}");

            try
            {
                // 1️⃣ Get all non-deleted equipments from repository
                var allItems = await _repository.GetAllAsync(); // assume this exists
                if (EnumerableExtensions.IsNullOrEmpty(allItems))
                {
                    _logger.Information("No equipment found in the database.");
                    return new List<Equipment>();
                }

                // 2️⃣ Filter in-memory by name (case-insensitive)
                var lowerName = name.Trim().ToLowerInvariant();
                var filtered = allItems
                    .Where(e => !e.IsDeleted && e.Name.ToLowerInvariant().Contains(lowerName))
                    .ToList();

                if (!filtered.Any())
                {
                    _logger.Information($"No equipments matched the search term '{name}'.");
                    return new List<Equipment>();
                }

                return filtered;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error while searching equipments by name: {name}");
                throw;
            }
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
