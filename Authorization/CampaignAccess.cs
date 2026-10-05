using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Authorization
{
    /// <summary>
    /// Single place that answers "may the current user touch this campaign / this entity".
    /// The superadmin (global "Admin" role) passes everything.
    /// </summary>
    public class CampaignAccess
    {
        public const string CampaignHeader = "X-Campaign-Id";
        public const string SuperAdminRole = "Admin";

        private readonly ICampaignRepository _campaigns;
        private readonly HttpContext? _http;
        private readonly Dictionary<string, Campaign?> _memo = new();
        private List<string>? _myCampaignIds;

        public CampaignAccess(ICampaignRepository campaigns, IHttpContextAccessor httpContextAccessor)
        {
            _campaigns = campaigns;
            _http = httpContextAccessor.HttpContext;
        }

        public ClaimsPrincipal? User => _http?.User;
        public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);
        public bool IsSuperAdmin => User?.IsInRole(SuperAdminRole) == true;

        /// <summary>Route value "campaignId" wins over the X-Campaign-Id header.</summary>
        public string? CurrentCampaignId
        {
            get
            {
                var id = _http?.GetRouteValue("campaignId")?.ToString();
                if (string.IsNullOrWhiteSpace(id))
                    id = _http?.Request.Headers[CampaignHeader].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(id)) return null;
                if (!ObjectId.TryParse(id, out _))
                    throw new ArgumentException($"Invalid campaign id '{id}'.");
                return id;
            }
        }

        public async Task<Campaign?> GetCampaignAsync(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (!_memo.TryGetValue(id, out var campaign))
                _memo[id] = campaign = await _campaigns.GetByIdAsync(id);
            return campaign;
        }

        public async Task<bool> IsMemberAsync(string? campaignId) =>
            IsSuperAdmin || (await GetCampaignAsync(campaignId))?.IsMember(UserId) == true;

        public async Task<bool> IsDmAsync(string? campaignId) =>
            IsSuperAdmin || (await GetCampaignAsync(campaignId))?.IsDm(UserId) == true;

        public async Task<Campaign> EnsureMemberAsync(string? campaignId)
        {
            var campaign = await GetCampaignAsync(campaignId) ?? throw new NotFoundException("Campaign not found.");
            if (!IsSuperAdmin && !campaign.IsMember(UserId))
                throw new ForbiddenException("You are not a member of this campaign.");
            return campaign;
        }

        public async Task<Campaign> EnsureDmAsync(string? campaignId)
        {
            var campaign = await GetCampaignAsync(campaignId) ?? throw new NotFoundException("Campaign not found.");
            if (!IsSuperAdmin && !campaign.IsDm(UserId))
                throw new ForbiddenException("Only the campaign's DM can do this.");
            return campaign;
        }

        /// <summary>Ids of the campaigns the current user belongs to.</summary>
        public async Task<List<string>> MyCampaignIdsAsync() =>
            _myCampaignIds ??= (await _campaigns.GetForMemberAsync(UserId ?? string.Empty)).Select(c => c.Id).ToList();

        public async Task<bool> CanReadAsync(ICampaignScoped entity)
        {
            if (IsSuperAdmin) return true;

            if (entity.CampaignId == null)
            {
                if (entity is not ICampaignContent) return true; // unassigned: ownership decides
                var type = CoreContentTypes.Of(entity.GetType());
                var current = await GetCampaignAsync(CurrentCampaignId);
                return type != null && current != null && current.IsMember(UserId) && current.CoreImports.Contains(type);
            }

            if (entity is not ICampaignContent && IsOwner(entity)) return true;
            return await IsMemberAsync(entity.CampaignId);
        }

        public async Task<bool> CanWriteAsync(ICampaignScoped entity)
        {
            if (IsSuperAdmin) return true;
            if (entity is ICampaignContent)
                return entity.CampaignId != null && await IsDmAsync(entity.CampaignId);
            return await CanReadAsync(entity);
        }

        public async Task EnsureCanReadAsync(ICampaignScoped entity)
        {
            if (!await CanReadAsync(entity))
                throw new ForbiddenException("This belongs to a campaign you are not part of.");
        }

        public async Task EnsureCanWriteAsync(ICampaignScoped entity)
        {
            if (!await CanWriteAsync(entity))
                throw new ForbiddenException(entity is ICampaignContent && entity.CampaignId == null
                    ? "Core content is read-only."
                    : "You are not allowed to change this.");
        }

        /// <summary>
        /// Prepares a new entity: fills CampaignId from the current campaign when missing,
        /// then checks the caller may write into it. A superadmin with no campaign selected creates core content.
        /// </summary>
        public async Task PrepareCreateAsync(ICampaignScoped entity)
        {
            if (string.IsNullOrWhiteSpace(entity.CampaignId))
                entity.CampaignId = CurrentCampaignId;

            if (entity.CampaignId == null && entity is ICampaignContent && !IsSuperAdmin)
                throw new ArgumentException("Select a campaign first.");

            if (entity.CampaignId != null)
            {
                if (entity is ICampaignContent) await EnsureDmAsync(entity.CampaignId);
                else await EnsureMemberAsync(entity.CampaignId);
            }
        }

        /// <summary>
        /// Query filter for list endpoints: the current campaign (plus imported core for content types).
        /// With no campaign selected: everything for the superadmin, otherwise the user's own campaigns.
        /// </summary>
        public async Task<FilterDefinition<T>> ReadFilterAsync<T>() where T : ICampaignScoped
        {
            var f = Builders<T>.Filter;
            // Raw documents: CampaignId is stored as ObjectId (or null = core / unassigned).
            static FilterDefinition<T> Is(BsonValue v) => new BsonDocument(nameof(ICampaignScoped.CampaignId), v);
            var isContent = typeof(ICampaignContent).IsAssignableFrom(typeof(T));
            var currentId = CurrentCampaignId;

            if (currentId == null)
            {
                if (IsSuperAdmin) return f.Empty;
                var mine = new BsonArray((await MyCampaignIdsAsync()).Select(ObjectId.Parse));
                var inMine = Is(new BsonDocument("$in", mine));
                // Unassigned non-content entities are still subject to ownership checks afterwards.
                return isContent ? inMine : inMine | Is(BsonNull.Value);
            }

            var campaign = await EnsureMemberAsync(currentId);
            var filter = Is(ObjectId.Parse(currentId));

            var type = CoreContentTypes.Of(typeof(T));
            if (isContent && type != null && (IsSuperAdmin || campaign.CoreImports.Contains(type)))
                filter |= Is(BsonNull.Value);

            return filter;
        }

        private bool IsOwner(object entity) =>
            entity is IOwnedResource owned && UserId != null && owned.OwnerIds?.Contains(UserId) == true;
    }
}
