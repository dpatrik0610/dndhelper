using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Database.Migrations
{
    /// <summary>
    /// Brings sessions from the old shape (IsLive flag, linked Note documents) to Status, Number and an embedded note log.
    /// Only documents without a Status are touched and old fields are left in place, so it is safe to run on every startup.
    /// </summary>
    public class SessionModelMigration
    {
        private readonly MongoDbContext _db;
        private readonly ILogger _logger;

        public SessionModelMigration(MongoDbContext db, ILogger logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<int> RunAsync()
        {
            var sessions = _db.GetCollection<BsonDocument>("Sessions");
            var legacy = await sessions.Find(Builders<BsonDocument>.Filter.Exists("Status", false)).ToListAsync();
            if (legacy.Count == 0) return 0;

            var notes = _db.GetCollection<BsonDocument>("Notes");
            var now = DateTime.UtcNow;

            foreach (var campaign in legacy.GroupBy(d => d.GetValue("CampaignId", BsonNull.Value).ToString()))
            {
                var number = 0;
                foreach (var doc in campaign.OrderBy(SortKey))
                {
                    number++;
                    var noteIds = doc.GetValue("NoteIds", new BsonArray()).AsBsonArray
                        .Where(v => v.IsString && ObjectId.TryParse(v.AsString, out _))
                        .Select(v => ObjectId.Parse(v.AsString))
                        .ToList();
                    var linked = noteIds.Count == 0
                        ? new()
                        : await notes.Find(Builders<BsonDocument>.Filter.In("_id", noteIds)).ToListAsync();

                    var set = new BsonDocument
                    {
                        { "Status", StatusOf(doc, now) },
                        { "Number", number },
                        { "Notes", new BsonArray(linked.Select(ToNote)) },
                    };
                    if (string.IsNullOrWhiteSpace(doc.GetValue("Name", "").ToString()))
                        set["Name"] = $"Session {number}";

                    await sessions.UpdateOneAsync(new BsonDocument("_id", doc["_id"]), new BsonDocument("$set", set));
                }
            }

            _logger.Information("Session model migration: {Count} sessions converted", legacy.Count);
            return legacy.Count;
        }

        private static DateTime SortKey(BsonDocument doc) =>
            DateOf(doc, "ScheduledFor") ?? DateOf(doc, "CreatedAt") ?? doc["_id"].AsObjectId.CreationTime;

        private static string StatusOf(BsonDocument doc, DateTime now)
        {
            if (doc.GetValue("IsLive", false).ToBoolean()) return "Live";
            if (DateOf(doc, "EndedAt") != null || DateOf(doc, "StartedAt") != null) return "Completed";
            return DateOf(doc, "ScheduledFor") is DateTime at && at < now ? "Completed" : "Planned";
        }

        private static DateTime? DateOf(BsonDocument doc, string field) =>
            doc.TryGetValue(field, out var v) && v.IsValidDateTime ? v.ToUniversalTime() : null;

        private static BsonDocument ToNote(BsonDocument note)
        {
            var title = note.GetValue("Title", "").ToString();
            var lines = note.GetValue("Lines", new BsonArray()).AsBsonArray.Select(l => l.ToString());
            var heading = string.IsNullOrWhiteSpace(title) || title == "No Title" ? "" : $"**{title}**\n";

            return new BsonDocument
            {
                { "Id", note["_id"].ToString() },
                { "Content", heading + string.Join("\n", lines) },
                { "DmOnly", false },
                { "Author", BsonNull.Value },
                { "CreatedAt", DateOf(note, "CreatedAt") ?? note["_id"].AsObjectId.CreationTime },
                { "UpdatedAt", BsonNull.Value },
            };
        }
    }
}
