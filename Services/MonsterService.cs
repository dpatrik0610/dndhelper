using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
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

        private readonly IFileStorageService _storage;
        private readonly IPublicDndApiClient _srd;
        private readonly IMemoryCache _cache;

        public MonsterService(IMonsterRepository repository, ILogger logger, IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor, IFileStorageService storage, IPublicDndApiClient srd, IMemoryCache cache)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _storage = storage;
            _srd = srd;
            _cache = cache;
        }

        public async Task<(List<Monster> Items, long Total)> SearchAsync(MonsterSearchCriteria criteria) =>
            await _repository.SearchAsync(await ScopeFilterAsync(), criteria);

        public async Task<List<string>> GetTypesAsync() => await _repository.GetTypesAsync(await ScopeFilterAsync());

        /// <summary>Old uploads are kept: tokens already on a board may still show them (same as character art).</summary>
        public async Task<Monster> UploadImageAsync(string monsterId, Stream content, string fileName)
        {
            var monster = await _repository.GetByIdAsync(monsterId) ?? throw new NotFoundException("Monster not found.");
            await EnsureWriteAccess(monster);
            var userId = GetCurrentUserId() ?? throw new UnauthorizedAccessException("Not signed in.");

            Monster? saved = null;
            await _storage.ReplaceAsync(null, content, StorageFolder.Monsters, userId, async file =>
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
