using dndhelper.Models;
using dndhelper.Repositories;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class SpellService : BaseService<Spell, ISpellRepository>, ISpellService
    {
        public SpellService(ISpellRepository repository, ILogger logger, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor) : base(repository, logger, authorizationService, httpContextAccessor)
        {
        }
        public async Task<List<SpellNameResponse>> GetAllNamesAsync()
        {
            // Here you could add any extra logic, filtering, caching, etc.
            return await _repository.GetAllNamesAsync();
        }
    }
}
