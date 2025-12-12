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
    public class RuleCategoryService : BaseService<RuleCategory, IRuleCategoryRepository>, IRuleCategoryService
    {
        public RuleCategoryService(IRuleCategoryRepository repository, ILogger logger, IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
        }

        public async Task<List<RuleCategoryDto>> GetCategoryListAsync()
        {
            var categories = await _repository.GetAllAsync();
            return categories
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Name)
                .Select(MapToDto)
                .ToList();
        }

        public async Task<RuleCategoryDto?> CreateCategoryAsync(RuleCategoryDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var entity = MapToEntity(dto);
            ValidateCategory(entity);
            await EnsureUniqueSlugAsync(entity.Slug, null);

            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;

            var created = await _repository.CreateAsync(entity);
            return created == null ? null : MapToDto(created);
        }

        public override async Task<RuleCategory?> CreateAsync(RuleCategory entity)
        {
            ValidateCategory(entity);
            await EnsureUniqueSlugAsync(entity.Slug, null);

            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            return await base.CreateAsync(entity);
        }

        public override async Task<RuleCategory?> UpdateAsync(RuleCategory entity)
        {
            ValidateCategory(entity);
            await EnsureUniqueSlugAsync(entity.Slug, entity.Id);

            entity.UpdatedAt = DateTime.UtcNow;
            return await base.UpdateAsync(entity);
        }

        private static RuleCategoryDto MapToDto(RuleCategory category) => new RuleCategoryDto
        {
            Id = category.Id,
            Slug = category.Slug,
            Name = category.Name,
            Description = category.Description,
            Order = category.Order
        };

        private static RuleCategory MapToEntity(RuleCategoryDto dto) => new RuleCategory
        {
            Id = dto.Id,
            Slug = dto.Slug.Trim().ToLowerInvariant(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Order = dto.Order
        };

        private void ValidateCategory(RuleCategory category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));

            if (string.IsNullOrWhiteSpace(category.Slug))
                throw new ArgumentException("Slug is required.");
            if (string.IsNullOrWhiteSpace(category.Name))
                throw new ArgumentException("Name is required.");
        }

        private async Task EnsureUniqueSlugAsync(string slug, string? excludeId)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("Slug is required.");

            var exists = await _repository.SlugExistsAsync(slug, excludeId);
            if (exists)
                throw new ArgumentException("Category slug must be unique.");
        }
    }
}
