using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace dndhelper.Repositories
{
    public class NoteRepository : MongoRepository<Note>, INoteRepository
    {
        public NoteRepository(ILogger logger, IMemoryCache cache, MongoDbContext context) : base(logger, cache, context, "Notes")
        {
        }
    }
}
