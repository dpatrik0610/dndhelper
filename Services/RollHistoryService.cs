using dndhelper.Models.RollModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Serilog;
using System;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class RollHistoryService : IRollHistoryService
    {
        private readonly IRollRepository _repository;
        private readonly ILogger _logger;

        public RollHistoryService(IRollRepository repository, ILogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<RollRecord?> CreateAsync(RollRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            var created = await _repository.CreateAsync(record);
            if (created == null)
            {
                _logger.Warning("Failed to persist roll record for user {UserId}", record.UserId);
            }

            return created;
        }
    }
}
