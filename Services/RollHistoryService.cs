using dndhelper.Models.RollModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class RollHistoryService : IRollHistoryService
    {
        private readonly IRollRepository _repository;
        private readonly ICampaignRepository _campaigns;
        private readonly IEntitySyncService _sync;
        private readonly ILogger _logger;

        public RollHistoryService(IRollRepository repository, ICampaignRepository campaigns, IEntitySyncService sync, ILogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _campaigns = campaigns ?? throw new ArgumentNullException(nameof(campaigns));
            _sync = sync ?? throw new ArgumentNullException(nameof(sync));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<RollRecord?> RecordAsync(RollRecord record)
        {
            Guard.NotNull(record, nameof(record));

            var created = await _repository.CreateAsync(record);
            if (created == null)
            {
                _logger.Warning("Failed to persist roll record for user {UserId}", record.UserId);
                return null;
            }

            try
            {
                // Live roll log: public rolls go to the whole table, subtle ones only to the DMs (and the roller).
                var recipients = new List<string> { created.UserId };
                var campaign = created.CampaignId == null ? null : await _campaigns.GetByIdAsync(created.CampaignId);
                if (campaign != null)
                    recipients.AddRange(created.Type == RollType.Subtle ? campaign.DmIds() : campaign.Members.Select(m => m.UserId));

                await _sync.BroadcastToUsers("RollRecorded", created, recipients);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to broadcast roll {RollId}", created.Id);
            }

            return created;
        }

        public async Task<IReadOnlyList<RollRecord>> GetHistoryAsync(string userId, string? campaignId, bool isDm, RollType? type, int page, int pageSize)
        {
            Guard.NotNullOrWhiteSpace(userId, nameof(userId));
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var f = Builders<RollRecord>.Filter;
            var filter = f.Eq(r => r.IsDeleted, false);

            if (campaignId == null)
                filter &= f.Eq(r => r.UserId, userId);
            else if (isDm)
                filter &= f.Eq(r => r.CampaignId, campaignId);
            else
                filter &= f.Eq(r => r.CampaignId, campaignId) & (f.Eq(r => r.Type, RollType.Public) | f.Eq(r => r.UserId, userId));

            if (type != null)
                filter &= f.Eq(r => r.Type, type.Value);

            return await _repository.QueryAsync(filter, (page - 1) * pageSize, pageSize);
        }
    }
}
