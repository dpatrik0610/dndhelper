using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace dndhelper.Models
{
    /// <summary>
    /// A campaign poll. The DM creates and manages it, every campaign member may vote.
    /// Votes are never sent to clients as-is: see <see cref="PollView"/>.
    /// </summary>
    public class Poll : IEntity, ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<PollOption> Options { get; set; } = new();

        public bool AllowMultiple { get; set; }
        /// <summary>Upper limit of picks when AllowMultiple is on; null = any number.</summary>
        public int? MaxChoices { get; set; }
        /// <summary>Nobody (not even the DM) sees who voted for what.</summary>
        public bool Anonymous { get; set; }
        public PollResultsVisibility ResultsVisibility { get; set; } = PollResultsVisibility.Always;
        public bool AllowVoteChange { get; set; } = true;
        /// <summary>Players may add their own options while the poll is open.</summary>
        public bool AllowSuggestions { get; set; }
        /// <summary>Send the result to every member when the poll closes.</summary>
        public bool AnnounceOnClose { get; set; } = true;

        public DateTime? ClosesAt { get; set; }
        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }

        public string? CreatedBy { get; set; }
        public List<PollVote> Votes { get; set; } = new();

        /// <summary>Optimistic concurrency token, bumped on every write.</summary>
        public int Version { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }

        public bool IsOpenAt(DateTime now) => !IsClosed && (ClosesAt == null || ClosesAt > now);
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PollResultsVisibility { Always, AfterVote, AfterClose }

    public class PollOption
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        /// <summary>Username of the player who suggested it; null for the DM's own options.</summary>
        public string? SuggestedBy { get; set; }
    }

    public class PollVote
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public List<string> OptionIds { get; set; } = new();
        public DateTime VotedAt { get; set; }
    }

    // ---- Requests ----

    public class PollUpsertRequest
    {
        public string? CampaignId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        /// <summary>Existing options keep their Id (and their votes); new ones come without one.</summary>
        public List<PollOption> Options { get; set; } = new();
        public bool AllowMultiple { get; set; }
        public int? MaxChoices { get; set; }
        public bool Anonymous { get; set; }
        public PollResultsVisibility ResultsVisibility { get; set; } = PollResultsVisibility.Always;
        public bool AllowVoteChange { get; set; } = true;
        public bool AllowSuggestions { get; set; }
        public bool AnnounceOnClose { get; set; } = true;
        public DateTime? ClosesAt { get; set; }
    }

    public class PollVoteRequest
    {
        public List<string> OptionIds { get; set; } = new();
    }

    public class PollSuggestionRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    // ---- Response ----

    /// <summary>What one user may see of a poll: hidden results and anonymous voters are stripped server-side.</summary>
    public class PollView
    {
        public string Id { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<PollOption> Options { get; set; } = new();
        public bool AllowMultiple { get; set; }
        public int? MaxChoices { get; set; }
        public bool Anonymous { get; set; }
        public PollResultsVisibility ResultsVisibility { get; set; }
        public bool AllowVoteChange { get; set; }
        public bool AllowSuggestions { get; set; }
        public bool AnnounceOnClose { get; set; }
        public DateTime? ClosesAt { get; set; }
        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public List<string> MyOptionIds { get; set; } = new();
        public int VoterCount { get; set; }
        public int MemberCount { get; set; }
        public bool ResultsHidden { get; set; }
        /// <summary>Votes per option id; null while results are hidden.</summary>
        public Dictionary<string, int>? Tally { get; set; }
        /// <summary>Who voted for what; null when anonymous or hidden.</summary>
        public List<PollVoterView>? Voters { get; set; }
    }

    public class PollVoterView
    {
        public string Username { get; set; } = string.Empty;
        public List<string> OptionIds { get; set; } = new();
        public DateTime VotedAt { get; set; }
    }
}
