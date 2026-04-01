using dndhelper.Models.RollModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
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

        public async Task<IReadOnlyList<RollRecord>> GetMyPublicRollsAsync(string userId, int page, int pageSize)
        {
            Guard.NotNullOrWhiteSpace(userId, nameof(userId));
            NormalizePaging(ref page, ref pageSize);

            var filter = Builders<RollRecord>.Filter.And(
                Builders<RollRecord>.Filter.Eq(r => r.UserId, userId),
                Builders<RollRecord>.Filter.Eq(r => r.Type, RollType.Public),
                Builders<RollRecord>.Filter.Eq(r => r.IsDeleted, false)
            );

            return await _repository.QueryAsync(filter, (page - 1) * pageSize, pageSize);
        }

        public async Task<IReadOnlyList<RollRecord>> GetRollsByCampaignAsync(string campaignId, int page, int pageSize)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));
            NormalizePaging(ref page, ref pageSize);

            var filter = Builders<RollRecord>.Filter.And(
                Builders<RollRecord>.Filter.Eq(r => r.CampaignId, campaignId),
                Builders<RollRecord>.Filter.Eq(r => r.IsDeleted, false)
            );

            return await _repository.QueryAsync(filter, (page - 1) * pageSize, pageSize);
        }

        private static void NormalizePaging(ref int page, ref int pageSize)
        {
            if (page < 1)
                page = 1;

            if (pageSize < 1)
                pageSize = 1;

            if (pageSize > 200)
                pageSize = 200;
        }
    }
}
