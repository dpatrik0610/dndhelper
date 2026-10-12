using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class MonsterService : BaseService<Monster, IMonsterRepository>, IMonsterService
    {
        public const int MaxImportBatch = 20;
        private static readonly Regex SrdIndex = new("^[a-z0-9-]{1,100}$");

        private const string DmOnly = "Monsters are for the campaign's DM.";

        private readonly IFileStorageService _storage;
        private readonly IPublicDndApiClient _srd;
        private readonly IMemoryCache _cache;
        private readonly ITabletopRepository _tables;
        private readonly IEncounterRepository _encounters;

        public MonsterService(IMonsterRepository repository, ILogger logger, IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor, IFileStorageService storage, IPublicDndApiClient srd, IMemoryCache cache,
            ITabletopRepository tables, IEncounterRepository encounters)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _storage = storage;
            _srd = srd;
            _cache = cache;
            _tables = tables;
            _encounters = encounters;
        }

        // Monsters are DM material: players never read stat blocks, and 5etools book content stays with its DM.

        public async Task<(List<Monster> Items, long Total)> SearchAsync(MonsterSearchCriteria criteria) =>
            await _repository.SearchAsync(await DmScopeAsync(), criteria);

        public async Task<List<string>> GetTypesAsync() => await _repository.GetTypesAsync(await DmScopeAsync());

        public override async Task<Monster?> GetByIdAsync(string id)
        {
            var monster = await base.GetByIdAsync(id);
            if (monster != null && !Access.IsSuperAdmin)
                await Access.EnsureDmAsync(monster.CampaignId ?? Access.CurrentCampaignId ?? throw new ForbiddenException(DmOnly));
            return monster;
        }

        /// <summary>The usual read scope, narrowed to campaigns the caller is DM of.</summary>
        private async Task<FilterDefinition<Monster>> DmScopeAsync()
        {
            var scope = await ScopeFilterAsync();
            if (Access.IsSuperAdmin) return scope;
            if (Access.CurrentCampaignId is string current)
            {
                await Access.EnsureDmAsync(current);
                return scope;
            }
            // No campaign picked: only the campaigns this user runs.
            return scope & Builders<Monster>.Filter.In(m => m.CampaignId, await Access.MyDmCampaignIdsAsync());
        }

        /// <summary>
        /// The old art is deleted once nothing else shows it. SRD imports share one image across same-name monsters,
        /// and tokens (on a table or saved in an encounter) keep the URL they were placed with.
        /// </summary>
        public async Task<Monster> UploadImageAsync(string monsterId, Stream content, string fileName)
        {
            var monster = await _repository.GetByIdAsync(monsterId) ?? throw new NotFoundException("Monster not found.");
            await EnsureWriteAccess(monster);
            var userId = GetCurrentUserId() ?? throw new UnauthorizedAccessException("Not signed in.");
            var oldFileId = await UnusedArtAsync(monster);

            Monster? saved = null;
            await _storage.ReplaceAsync(oldFileId, content, StorageFolder.Monsters, userId, async file =>
            {
                monster.ImageUrl = PublicUrl(file);
                saved = await UpdateAsync(monster) ?? throw new InvalidOperationException("Could not save the monster.");
            }, fileName);
            return saved!;
        }

        // --- SRD import (superadmin; the controller enforces the role). Lands in core content. ---

        public async Task<List<SrdMonsterEntry>> GetSrdCatalogAsync(string ruleset)
        {
            EnsureRuleset(ruleset);
            var index = await _cache.GetOrCreateAsync($"srd-monsters-{ruleset}", e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                return _srd.GetMonsterIndexAsync(ruleset);
            }) ?? new();

            var byName = (await _repository.FindByNamesAsync(index.Select(i => i.Name)))
                .ToLookup(m => m.Name, StringComparer.OrdinalIgnoreCase);

            return index.Select(i => new SrdMonsterEntry(i.Index, i.Name,
                byName[i.Name].Any(m => m.CampaignId == null), byName[i.Name].All(m => m.ImageUrl != null))).ToList();
        }

        /// <summary>
        /// Imports SRD monsters into core; a name already in core is not duplicated. Same-name monsters
        /// anywhere (core or a campaign) that have no art get the SRD image.
        /// </summary>
        public async Task<SrdImportResult> ImportSrdAsync(string ruleset, List<string> keys)
        {
            EnsureRuleset(ruleset);
            keys = keys.Distinct().ToList();
            if (keys.Count == 0 || keys.Count > MaxImportBatch)
                throw new ArgumentException($"Import 1 to {MaxImportBatch} monsters at a time.");
            if (keys.FirstOrDefault(k => !SrdIndex.IsMatch(k)) is string bad)
                throw new ArgumentException($"'{bad}' is not an SRD monster key.");
            var userId = GetCurrentUserId() ?? throw new UnauthorizedAccessException("Not signed in.");

            // Network first, in parallel; database and R2 writes stay sequential.
            var fetched = await Task.WhenAll(keys.Select(async key =>
            {
                try
                {
                    var json = await _srd.GetMonsterAsync(ruleset, key);
                    var image = json?["image"] is JValue { Type: JTokenType.String } path ? await _srd.GetImageAsync((string)path!) : null;
                    return (key, json, image);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not fetch SRD monster {Key}", key);
                    return (key, json: (JObject?)null, image: (byte[]?)null);
                }
            }));

            var result = new SrdImportResult();
            var existing = (await _repository.FindByNamesAsync(fetched.Where(f => f.json != null).Select(f => (string)f.json!["name"]!)))
                .ToLookup(m => m.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var (key, json, image) in fetched)
            {
                if (json == null) { result.Failed.Add($"{key}: not found"); continue; }
                var monster = SrdMonsterMapper.Map(json, ruleset);
                var same = existing[monster.Name].ToList();
                var inCore = same.Any(m => m.CampaignId == null);
                var needImage = !inCore || same.Any(m => m.ImageUrl == null);

                try
                {
                    var imageUrl = needImage && image != null ? await StoreImageAsync(image, userId, key) : null;

                    if (inCore) result.Skipped++;
                    else
                    {
                        monster.CampaignId = null; // core
                        monster.ImageUrl = imageUrl;
                        if (await _repository.CreateAsync(monster) == null) throw new InvalidOperationException("Insert failed.");
                        result.Created++;
                    }

                    if (imageUrl == null) continue;
                    foreach (var m in same.Where(m => m.ImageUrl == null))
                    {
                        m.ImageUrl = imageUrl;
                        await _repository.UpdateAsync(m);
                        result.ImagesAdded++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not import SRD monster {Key}", key);
                    result.Failed.Add($"{key}: {ex.Message}");
                }
            }

            _logger.Information("SRD {Ruleset} monster import: {@Result}", ruleset, result);
            return result;
        }

        /// <summary>The stored file behind this monster's art, if no other monster or token uses it; else null (keep it).</summary>
        private async Task<string?> UnusedArtAsync(Monster monster)
        {
            if (monster.ImageUrl is not string url) return null;
            var file = await _storage.GetByUrlAsync(url);
            if (file?.Id == null || file.Folder != StorageFolder.Monsters) return null;

            var shared = (await _repository.FindAsync(Builders<Monster>.Filter.Eq(m => m.ImageUrl, url))).Any(m => m.Id != monster.Id)
                || await _tables.AnyUsesTokenImageAsync(url)
                || (await _encounters.FindAsync(Builders<Encounter>.Filter.Eq("Board.Tokens.ImageUrl", url))).Count > 0;
            return shared ? null : file.Id;
        }

        private async Task<string?> StoreImageAsync(byte[] image, string userId, string key)
        {
            try
            {
                await using var content = new MemoryStream(image);
                return PublicUrl(await _storage.UploadAsync(content, StorageFolder.Monsters, userId, $"{key}.png"));
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // A monster without art is still worth importing.
                _logger.Warning(ex, "Could not store the SRD image for {Key}", key);
                return null;
            }
        }

        private static string PublicUrl(StoredFile file) =>
            file.Url ?? throw new InvalidOperationException("Monster images need a public R2 domain (R2__PublicBaseUrl).");

        private static void EnsureRuleset(string ruleset)
        {
            if (!SrdMonsterMapper.Rulesets.ContainsKey(ruleset))
                throw new ArgumentException($"Unknown ruleset '{ruleset}'. Use {string.Join(" or ", SrdMonsterMapper.Rulesets.Keys)}.");
        }
    }

    public record SrdMonsterEntry(string Key, string Name, bool Exists, bool HasImage);

    public class SrdImportResult
    {
        public int Created { get; set; }
        /// <summary>Already in core under this name, so not duplicated.</summary>
        public int Skipped { get; set; }
        public int ImagesAdded { get; set; }
        public List<string> Failed { get; set; } = new();
    }
}
