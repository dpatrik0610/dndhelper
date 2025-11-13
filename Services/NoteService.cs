using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace dndhelper.Services
{
    public class NoteService : BaseService<Note, INoteRepository>, INoteService
    {
        public NoteService(INoteRepository repository, ILogger logger, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor) : base(repository, logger, authorizationService, httpContextAccessor)
        {
        }
    }
}
