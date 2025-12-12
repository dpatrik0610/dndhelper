using dndhelper.Models.RuleModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class RuleService : BaseService<Rule, IRuleRepository>, IRuleService
    {
        private readonly IRuleCategoryRepository _categoryRepository;

        public RuleService(IRuleRepository repository, IRuleCategoryRepository categoryRepository, ILogger logger, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
        }

        public async Task<RuleListResponse> GetListAsync(RuleQueryOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var normalizedOptions = NormalizeOptions(options);
            var result = await _repository.QueryAsync(normalizedOptions);

            return new RuleListResponse
            {
                Items = result.Items.Select(MapToSnippetDto).ToList(),
                Total = result.Total,
                NextCursor = result.NextCursor
            };
        }

        public async Task<RuleDetailDto?> GetBySlugAsync(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentNullException(nameof(slug));

            var rule = await _repository.GetBySlugAsync(slug);
            return rule == null ? null : MapToDetailDto(rule);
        }

        public async Task<RuleDetailResponse?> GetDetailAsync(string slug)
        {
            var detail = await GetBySlugAsync(slug);
            return detail == null ? null : new RuleDetailResponse { Rule = detail };
        }

        public async Task<RuleStats> GetStatsAsync()
        {
            return await _repository.GetStatsAsync();
        }

        public async Task<RuleDetailDto?> CreateRuleAsync(RuleDetailDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var entity = MapToEntity(dto);
            ValidateRule(entity);
            await EnsureCategoryExistsAsync(entity.Category);
            await EnsureUniqueSlugAsync(entity.Slug, null);

            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.IsDeleted = false;

            var created = await _repository.CreateAsync(entity);
            return created == null ? null : MapToDetailDto(created);
        }

        public async Task<RuleDetailDto?> UpdateRuleAsync(string slug, RuleDetailDto dto)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentNullException(nameof(slug));
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var existing = await _repository.GetBySlugAsync(slug);
            if (existing == null)
                return null;

            ApplyDtoToEntity(dto, existing);
            ValidateRule(existing);
            await EnsureCategoryExistsAsync(existing.Category);
            await EnsureUniqueSlugAsync(existing.Slug, existing.Id);

            existing.UpdatedAt = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(existing);
            return updated == null ? null : MapToDetailDto(updated);
        }

        public override async Task<Rule?> CreateAsync(Rule entity)
        {
            ValidateRule(entity);
            await EnsureCategoryExistsAsync(entity.Category);
            await EnsureUniqueSlugAsync(entity.Slug, null);

            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.IsDeleted = false;

            return await _repository.CreateAsync(entity);
        }

        public override async Task<Rule?> UpdateAsync(Rule entity)
        {
            ValidateRule(entity);
            await EnsureCategoryExistsAsync(entity.Category);
            await EnsureUniqueSlugAsync(entity.Slug, entity.Id);

            entity.UpdatedAt = DateTime.UtcNow;
            return await _repository.UpdateAsync(entity);
        }

        private static RuleQueryOptions NormalizeOptions(RuleQueryOptions options)
        {
            options.Limit = options.Limit <= 0 ? 20 : Math.Min(options.Limit, 100);
            options.Category = options.Category?.Trim().ToLowerInvariant();
            options.Tag = options.Tag?.Trim();
            options.Source = options.Source?.Trim();
            options.Search = options.Search?.Trim();
            options.Cursor = options.Cursor?.Trim();
            return options;
        }

        private static Rule MapToEntity(RuleDetailDto dto)
        {
            return new Rule
            {
                Id = dto.Id,
                Slug = dto.Slug,
                Title = dto.Title,
                Category = dto.Category?.Trim().ToLowerInvariant() ?? string.Empty,
                Summary = dto.Summary,
                Tags = dto.Tags ?? new List<string>(),
                UpdatedAt = DateTime.UtcNow,
                Source = dto.Source,
                Body = dto.Body ?? new List<string>(),
                Sources = dto.Sources ?? new List<RuleSource>(),
                Examples = dto.Examples ?? new List<RuleExample>(),
                References = dto.References ?? new List<RuleReference>(),
                RelatedRuleSlugs = dto.RelatedRuleSlugs ?? new List<string>()
            };
        }

        private static void ApplyDtoToEntity(RuleDetailDto dto, Rule entity)
        {
            entity.Slug = dto.Slug;
            entity.Title = dto.Title;
            entity.Category = dto.Category?.Trim().ToLowerInvariant() ?? string.Empty;
            entity.Summary = dto.Summary;
            entity.Tags = dto.Tags ?? new List<string>();
            entity.Body = dto.Body ?? new List<string>();
            entity.Source = dto.Source;
            entity.Sources = dto.Sources ?? new List<RuleSource>();
            entity.Examples = dto.Examples ?? new List<RuleExample>();
            entity.References = dto.References ?? new List<RuleReference>();
            entity.RelatedRuleSlugs = dto.RelatedRuleSlugs ?? new List<string>();
        }

        private static RuleSnippetDto MapToSnippetDto(Rule rule)
        {
            return new RuleSnippetDto
            {
                Id = rule.Id,
                Slug = rule.Slug,
                Title = rule.Title,
                Category = rule.Category,
                Summary = rule.Summary,
                Tags = rule.Tags ?? new List<string>(),
                UpdatedAt = rule.UpdatedAt?.ToUniversalTime().ToString("o"),
                Source = rule.Source
            };
        }

        private static RuleDetailDto MapToDetailDto(Rule rule)
        {
            var snippet = MapToSnippetDto(rule);
            return new RuleDetailDto
            {
                Id = snippet.Id,
                Slug = snippet.Slug,
                Title = snippet.Title,
                Category = snippet.Category,
                Summary = snippet.Summary,
                Tags = snippet.Tags,
                UpdatedAt = snippet.UpdatedAt,
                Source = snippet.Source,
                Body = rule.Body ?? new List<string>(),
                Sources = rule.Sources ?? new List<RuleSource>(),
                Examples = rule.Examples ?? new List<RuleExample>(),
                References = rule.References ?? new List<RuleReference>(),
                RelatedRuleSlugs = rule.RelatedRuleSlugs ?? new List<string>()
            };
        }

        private void ValidateRule(Rule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            if (string.IsNullOrWhiteSpace(rule.Slug))
                throw new ArgumentException("Slug is required.");
            if (string.IsNullOrWhiteSpace(rule.Title))
                throw new ArgumentException("Title is required.");
            if (string.IsNullOrWhiteSpace(rule.Category))
                throw new ArgumentException("Category is required.");
            if (string.IsNullOrWhiteSpace(rule.Summary))
                throw new ArgumentException("Summary is required.");
            if (rule.Tags == null || !rule.Tags.Any())
                throw new ArgumentException("At least one tag is required.");
        }

        private async Task EnsureCategoryExistsAsync(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("Category is required.");

            var existingCategories = await _categoryRepository.GetAllAsync();
            var match = existingCategories.FirstOrDefault(c =>
                c.Slug.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals(category, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                throw new ArgumentException($"Category '{category}' does not exist. Create it first.");
        }

        private async Task EnsureUniqueSlugAsync(string slug, string? excludeId)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("Slug is required.");

            var exists = await _repository.SlugExistsAsync(slug, excludeId);
            if (exists)
            {
                throw new ArgumentException("Slug must be unique.");
            }
        }
    }
}
