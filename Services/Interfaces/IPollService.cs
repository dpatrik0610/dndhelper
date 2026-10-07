using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IPollService
    {
        Task<List<PollView>> GetByCampaignAsync(string campaignId);
        Task<PollView> GetPollAsync(string id);

        // DM
        Task<PollView> CreatePollAsync(PollUpsertRequest request);
        Task<PollView> UpdatePollAsync(string id, PollUpsertRequest request);
        Task<bool> DeletePollAsync(string id);
        Task<PollView> ClosePollAsync(string id);
        Task<PollView> ReopenPollAsync(string id);
        Task AnnounceAsync(string id);

        // Members
        Task<PollView> VoteAsync(string id, PollVoteRequest request);
        Task<PollView> RetractVoteAsync(string id);
        Task<PollView> SuggestOptionAsync(string id, PollSuggestionRequest request);
    }
}
