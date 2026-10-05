using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace dndhelper.Database.Migrations
{
    /// <summary>
    /// One-shot move to campaign-scoped data. Idempotent: every step only touches documents that
    /// are missing what it adds, so re-running reports zero changes. Nothing is deleted.
    /// Take a backup (Backups tab / GET api/backup/all) before running it for real.
    /// </summary>
    public class CampaignScopeMigration
    {
        public const string SuperAdminUsername = "qantanas";
        private static readonly string[] CoreTypes = { "Spells", "Equipment", "Monsters", "Rules" };
        private static readonly string[] ContentCollections = { "Spells", "Equipment", "Monsters", "Rules" };

        private readonly MongoDbContext _db;
        private readonly ILogger _logger;

        public CampaignScopeMigration(MongoDbContext db, ILogger logger)
        {
            _db = db;
            _logger = logger;
        }

        private IMongoCollection<BsonDocument> C(string name) => _db.GetCollection<BsonDocument>(name);

        // Matches documents where the field is missing or null.
        private static FilterDefinition<BsonDocument> Missing(string field) =>
            Builders<BsonDocument>.Filter.Eq(field, BsonNull.Value);

        public async Task<Dictionary<string, long>> RunAsync(string targetCampaignId, bool dryRun)
        {
            if (!ObjectId.TryParse(targetCampaignId, out var target))
                throw new ArgumentException("targetCampaignId must be a campaign id.");
            if (await C("Campaigns").CountDocumentsAsync(new BsonDocument("_id", target)) == 0)
                throw new ArgumentException("Target campaign not found.");

            var superAdmin = await C("Users").Find(new BsonDocument("Username", SuperAdminUsername)).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException($"User '{SuperAdminUsername}' not found.");
            var superAdminId = superAdmin["_id"].AsObjectId;

            var report = new Dictionary<string, long>();

            report["Users.Roles"] = await MigrateUserRolesAsync(superAdminId, dryRun);
            report["Campaigns.Members"] = await MigrateCampaignMembersAsync(superAdminId, dryRun);
            report["Characters.CampaignId"] = await MigrateCharactersAsync(target, dryRun);
            report["Inventories.CampaignId"] = await MigrateInventoriesAsync(target, dryRun);
            report["Notes.CampaignId"] = await MigrateNotesAsync(target, dryRun);
            foreach (var name in ContentCollections)
                report[$"{name}.CampaignId"] = await SetMissingAsync(C(name), Missing("CampaignId"), target, dryRun);
            report["Sessions.CampaignId(string->ObjectId)"] = await MigrateSessionIdsAsync(dryRun);

            _logger.Information("Campaign scope migration {Mode}: {@Report}", dryRun ? "dry run" : "applied", report);
            return report;
        }

        private async Task<long> MigrateUserRolesAsync(ObjectId superAdminId, bool dryRun)
        {
            var users = C("Users");
            var admin = new BsonArray { "Admin" };
            var user = new BsonArray { "User" };
            var f = Builders<BsonDocument>.Filter;

            var adminFilter = f.Eq("_id", superAdminId) & f.Ne("Roles", admin);
            var othersFilter = f.Ne("_id", superAdminId) & f.Ne("Roles", user);

            if (dryRun)
                return await users.CountDocumentsAsync(adminFilter) + await users.CountDocumentsAsync(othersFilter);

            var a = await users.UpdateManyAsync(adminFilter, Builders<BsonDocument>.Update.Set("Roles", admin));
            var o = await users.UpdateManyAsync(othersFilter, Builders<BsonDocument>.Update.Set("Roles", user));
            return a.ModifiedCount + o.ModifiedCount;
        }

        private async Task<long> MigrateCampaignMembersAsync(ObjectId superAdminId, bool dryRun)
        {
            var f = Builders<BsonDocument>.Filter;
            var filter = f.Or(f.Exists("Members", false), f.Size("Members", 0));
            var campaigns = await C("Campaigns").Find(filter).ToListAsync();
            if (dryRun) return campaigns.Count;

            foreach (var campaign in campaigns)
            {
                // userId -> roles, insertion-ordered
                var members = new List<(ObjectId UserId, string Role)>();
                void Add(ObjectId id, string role)
                {
                    if (!members.Any(m => m.UserId == id)) members.Add((id, role));
                }

                foreach (var owner in ArrayOf(campaign, "OwnerIds"))
                    if (ObjectId.TryParse(owner.ToString(), out var id)) Add(id, "DM");
                if (members.Count == 0) Add(superAdminId, "DM");

                var characterIds = ArrayOf(campaign, "CharacterIds").Select(ToObjectId).Where(x => x != null).Cast<ObjectId>();
                var characters = await C("Characters").Find(f.In("_id", characterIds)).ToListAsync();
                foreach (var owner in characters.SelectMany(c => ArrayOf(c, "OwnerIds")))
                    if (ObjectId.TryParse(owner.ToString(), out var id)) Add(id, "Player");

                var memberDocs = new BsonArray(members.Select(m => new BsonDocument
                {
                    { "UserId", m.UserId },
                    { "Roles", new BsonArray { m.Role } }
                }));
                var dmIds = new BsonArray(members.Where(m => m.Role == "DM").Select(m => m.UserId.ToString()));

                var update = Builders<BsonDocument>.Update
                    .Set("Members", memberDocs)
                    .Set("OwnerIds", dmIds)
                    .Set("CoreImports", new BsonArray(CoreTypes));
                if (!campaign.Contains("InviteCode") || campaign["InviteCode"].IsBsonNull)
                    update = update.Set("InviteCode", RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 8));

                await C("Campaigns").UpdateOneAsync(f.Eq("_id", campaign["_id"]), update);
            }
            return campaigns.Count;
        }

        private async Task<long> MigrateCharactersAsync(ObjectId target, bool dryRun)
        {
            var characters = await C("Characters").Find(Missing("CampaignId")).ToListAsync();
            if (dryRun) return characters.Count;

            var byCharacter = await CampaignByListedIdAsync("CharacterIds");
            foreach (var c in characters)
                await SetCampaignAsync(C("Characters"), c["_id"], byCharacter.GetValueOrDefault(c["_id"].ToString()!, target));
            return characters.Count;
        }

        private async Task<long> MigrateInventoriesAsync(ObjectId target, bool dryRun)
        {
            var inventories = await C("Inventories").Find(Missing("CampaignId")).ToListAsync();
            if (dryRun) return inventories.Count;

            var characterCampaign = await FieldByIdAsync("Characters", "CampaignId");
            foreach (var inv in inventories)
            {
                var campaign = ArrayOf(inv, "CharacterIds")
                    .Select(id => Get(characterCampaign, id.ToString()!))
                    .FirstOrDefault(id => id != null) ?? target;
                await SetCampaignAsync(C("Inventories"), inv["_id"], campaign);
            }
            return inventories.Count;
        }

        private async Task<long> MigrateNotesAsync(ObjectId target, bool dryRun)
        {
            var notes = await C("Notes").Find(Missing("CampaignId")).ToListAsync();
            if (dryRun) return notes.Count;

            // Parent lookup order: campaign, session, character.
            var byCampaign = await CampaignByListedIdAsync("NoteIds");
            var sessionCampaign = await FieldByIdAsync("Sessions", "CampaignId");
            var characterCampaign = await FieldByIdAsync("Characters", "CampaignId");
            var bySession = await ParentFieldByListedIdAsync("Sessions", "NoteIds", sessionCampaign);
            var byCharacter = await ParentFieldByListedIdAsync("Characters", "NoteIds", characterCampaign);

            foreach (var note in notes)
            {
                var id = note["_id"].ToString()!;
                var campaign = Get(byCampaign, id) ?? Get(bySession, id) ?? Get(byCharacter, id) ?? target;
                await SetCampaignAsync(C("Notes"), note["_id"], campaign);
            }
            return notes.Count;
        }

        private async Task<long> MigrateSessionIdsAsync(bool dryRun)
        {
            var filter = Builders<BsonDocument>.Filter.Type("CampaignId", BsonType.String);
            var sessions = await C("Sessions").Find(filter).ToListAsync();
            if (dryRun) return sessions.Count;

            long changed = 0;
            foreach (var s in sessions)
            {
                if (!ObjectId.TryParse(s["CampaignId"].AsString, out var id)) continue; // leave junk for manual review
                await SetCampaignAsync(C("Sessions"), s["_id"], id);
                changed++;
            }
            return changed;
        }

        private static async Task<long> SetMissingAsync(IMongoCollection<BsonDocument> col, FilterDefinition<BsonDocument> filter, ObjectId value, bool dryRun)
        {
            if (dryRun) return await col.CountDocumentsAsync(filter);
            return (await col.UpdateManyAsync(filter, Builders<BsonDocument>.Update.Set("CampaignId", value))).ModifiedCount;
        }

        private static Task SetCampaignAsync(IMongoCollection<BsonDocument> col, BsonValue id, ObjectId campaignId) =>
            col.UpdateOneAsync(new BsonDocument("_id", id), Builders<BsonDocument>.Update.Set("CampaignId", campaignId));

        /// <summary>Listed child id -> owning campaign id, from a Campaigns list field.</summary>
        private async Task<Dictionary<string, ObjectId>> CampaignByListedIdAsync(string listField)
        {
            var map = new Dictionary<string, ObjectId>();
            foreach (var c in await C("Campaigns").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
                foreach (var child in ArrayOf(c, listField))
                    map.TryAdd(child.ToString()!, c["_id"].AsObjectId);
            return map;
        }

        /// <summary>Document id -> ObjectId value of a field (strings are parsed).</summary>
        private async Task<Dictionary<string, ObjectId>> FieldByIdAsync(string collection, string field)
        {
            var map = new Dictionary<string, ObjectId>();
            foreach (var d in await C(collection).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
                if (d.Contains(field) && ToObjectId(d[field]) is { } v)
                    map[d["_id"].ToString()!] = v;
            return map;
        }

        /// <summary>Listed child id -> parent's mapped value, e.g. note id -> its session's campaign.</summary>
        private async Task<Dictionary<string, ObjectId>> ParentFieldByListedIdAsync(string collection, string listField, Dictionary<string, ObjectId> parentValue)
        {
            var map = new Dictionary<string, ObjectId>();
            foreach (var d in await C(collection).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
                if (parentValue.TryGetValue(d["_id"].ToString()!, out var value))
                    foreach (var child in ArrayOf(d, listField))
                        map.TryAdd(child.ToString()!, value);
            return map;
        }

        private static ObjectId? Get(Dictionary<string, ObjectId> map, string key) =>
            map.TryGetValue(key, out var v) ? v : null;

        private static IEnumerable<BsonValue> ArrayOf(BsonDocument doc, string field) =>
            doc.Contains(field) && doc[field].IsBsonArray ? doc[field].AsBsonArray : Enumerable.Empty<BsonValue>();

        private static ObjectId? ToObjectId(BsonValue v) =>
            v.IsObjectId ? v.AsObjectId : v.IsString && ObjectId.TryParse(v.AsString, out var id) ? id : null;
    }
}
