using dndhelper.Authorization;
using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class ShopService : BaseService<Shop, IShopRepository>, IShopService
    {
        private readonly ICampaignRepository _campaignRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly ICurrencyService _currencyService;
        private readonly IEquipmentRepository _equipmentRepository;
        private readonly ICharacterRepository _characterRepository;
        private readonly ISellRequestRepository _sellRequestRepository;
        private readonly IEntitySyncService _entitySyncService;

        public ShopService(
            IShopRepository repository,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            ICampaignRepository campaignRepository,
            IInventoryRepository inventoryRepository,
            ICurrencyService currencyService,
            IEquipmentRepository equipmentRepository,
            ICharacterRepository characterRepository,
            ISellRequestRepository sellRequestRepository,
            IEntitySyncService entitySyncService) 
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _campaignRepository = campaignRepository;
            _inventoryRepository = inventoryRepository;
            _currencyService = currencyService;
            _equipmentRepository = equipmentRepository;
            _characterRepository = characterRepository;
            _sellRequestRepository = sellRequestRepository;
            _entitySyncService = entitySyncService;
        }

        public async Task<IEnumerable<Shop>> GetShopsForCampaignAsync(string campaignId)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));
            var campaign = await _campaignRepository.GetByIdAsync(campaignId)
                ?? throw new KeyNotFoundException("Campaign not found.");

            var shops = await _repository.GetByCampaignIdAsync(campaignId);
            var userId = GetCurrentUserId();
            bool isCampaignOwner = campaign.OwnerIds?.Contains(userId) == true;

            return isCampaignOwner ? shops : shops.Where(s => s.IsOpened);
        }

        public async Task<Shop?> CreateShopWithInventoryAsync(Shop shop)
        {
            if (shop == null) throw new ArgumentNullException(nameof(shop));

            var shopInventory = new Inventory
            {
                Name = $"{shop.Name} Stock",
                CampaignId = shop.CampaignId,
                OwnerIds = shop.OwnerIds.ToList(),
                Currencies = new List<Currency>
                {
                    new Currency { Type = "gp", Amount = 100, CurrencyCode = "gp" },
                    new Currency { Type = "sp", Amount = 100, CurrencyCode = "sp" }
                }
            };

            var createdInventory = await _inventoryRepository.CreateAsync(shopInventory);
            if (createdInventory == null || string.IsNullOrEmpty(createdInventory.Id))
            {
                throw new Exception("Failed to create shop register.");
            }

            shop.InventoryId = createdInventory.Id;
            return await CreateAsync(shop);
        }

        public async Task<Shop?> ToggleShopOpenStatusAsync(string shopId, bool isOpened)
        {
            var shop = await _repository.GetByIdAsync(shopId);
            if (shop == null) return null;
            
            await EnsureOwnershipAccess(shop);

            shop.IsOpened = isOpened;
            shop.UpdatedAt = DateTime.UtcNow;
            return await _repository.UpdateAsync(shop);
        }

        public async Task<bool> BuyItemFromShopAsync(string shopId, string buyerCharacterId, string equipmentId, int quantity)
        {
            var shop = await _repository.GetByIdAsync(shopId) ?? throw new KeyNotFoundException("Shop not found.");
            if (!shop.IsOpened) throw new InvalidOperationException("The shop is closed.");

            var character = await _characterRepository.GetByIdAsync(buyerCharacterId) ?? throw new KeyNotFoundException("Character not found.");
            await EnsureOwnershipAccess(character);

            var shopInventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId) ?? throw new KeyNotFoundException("Shop inventory not found.");
            var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == equipmentId) ?? throw new KeyNotFoundException("Item not sold here.");

            if (shopItem.Quantity.HasValue && shopItem.Quantity.Value < quantity)
            {
                throw new InvalidOperationException("Insufficient shop stock.");
            }

            var equipment = await _equipmentRepository.GetByIdAsync(equipmentId) ?? throw new KeyNotFoundException("Equipment specifications not found.");
            
            // Convert GP to SP base value using integer math
            int baseCostSp = ConvertCostToSilver(equipment.Cost); 
            int totalCostSp = (int)Math.Round(baseCostSp * shop.PriceMultiplier * quantity);

            // Combined wallet gold/silver pool validation
            _currencyService.ValidateCharacterHasFunds(character, totalCostSp);

            // Deduct character funds
            _currencyService.DeductAndConsolidateCurrency(character, totalCostSp);
            character = await _characterRepository.UpdateAsync(character) ?? character;

            // Add currency to shop register
            _currencyService.AddCurrencyToInventory(shopInventory, totalCostSp);

            // Transfer item quantity from shop to character
            if (shopItem.Quantity.HasValue) 
            {
                shopItem.Quantity -= quantity;
                if (shopItem.Quantity.Value <= 0)
                {
                    shopInventory.Items?.Remove(shopItem);
                }
            }
            await _inventoryRepository.UpdateAsync(shopInventory);

            string playerInventoryId = character.InventoryIds?.FirstOrDefault() ?? throw new InvalidOperationException("Character has no bag.");
            var playerInventory = await _inventoryRepository.GetByIdAsync(playerInventoryId) ?? throw new KeyNotFoundException("Player bag not found.");
            
            // Add item to player inventory bag
            var existingItem = playerInventory.Items?.FirstOrDefault(i => i.EquipmentId == equipmentId);
            if (existingItem != null)
            {
                existingItem.Quantity = (existingItem.Quantity ?? 0) + quantity;
            }
            else
            {
                playerInventory.Items ??= new List<InventoryItem>();
                playerInventory.Items.Add(new InventoryItem { EquipmentId = equipmentId, EquipmentName = equipment.Name, Quantity = quantity });
            }
            await _inventoryRepository.UpdateAsync(playerInventory);

            // Broadcast real-time updates over SignalR
            await BroadcastCharacterChangeAsync(character, "updated", character);
            await BroadcastInventoryChangeAsync(shopInventory, "updated", shopInventory);
            await BroadcastInventoryChangeAsync(playerInventory, "updated", playerInventory);

            return true;
        }

        public async Task<SellRequest> SubmitSellRequestAsync(SellRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var character = await _characterRepository.GetByIdAsync(request.CharacterId)
                ?? throw new KeyNotFoundException("Character not found.");
            await EnsureOwnershipAccess(character);

            var campaign = await _campaignRepository.GetByIdAsync(request.CampaignId)
                ?? throw new KeyNotFoundException("Campaign not found.");

            var owners = new List<string> { GetCurrentUserId() };
            if (campaign.OwnerIds != null)
            {
                owners.AddRange(campaign.OwnerIds);
            }
            request.OwnerIds = owners.Distinct().ToList();
            request.Status = SellRequestStatus.Pending;

            if (request.IsSteal)
            {
                // STEAL FLOW: Verify item exists in Shop stock before submitting attempt
                var shop = await _repository.GetByIdAsync(request.ShopId)
                    ?? throw new KeyNotFoundException("Shop not found.");

                var shopInventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId)
                    ?? throw new KeyNotFoundException("Shop stock register not found.");

                var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                if (shopItem == null || (shopItem.Quantity ?? 0) < request.Quantity)
                {
                    throw new InvalidOperationException("The shop has insufficient stock of this item to steal.");
                }

                // Set OfferedPriceGp to 0 for steal requests
                request.OfferedPriceGp = 0;

                var created = await _sellRequestRepository.CreateAsync(request);

                return created;
            }
            else
            {
                // STANDARD SELL FLOW: Escrow item from Player bag
                string playerInventoryId = character.InventoryIds?.FirstOrDefault()
                    ?? throw new InvalidOperationException("Character has no inventory bag.");

                var playerInventory = await _inventoryRepository.GetByIdAsync(playerInventoryId)
                    ?? throw new KeyNotFoundException("Player inventory bag not found.");

                var bagItem = playerInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                if (bagItem == null || (bagItem.Quantity ?? 0) < request.Quantity)
                {
                    throw new InvalidOperationException("Character has insufficient item stock to sell.");
                }

                // Escrow item immediately: deduct from player bag
                bagItem.Quantity -= request.Quantity;
                if (bagItem.Quantity <= 0) 
                {
                    playerInventory.Items.Remove(bagItem);
                }
                
                await _inventoryRepository.UpdateAsync(playerInventory);
                var created = await _sellRequestRepository.CreateAsync(request);

                // Broadcast escrow update
                await BroadcastInventoryChangeAsync(playerInventory, "updated", playerInventory);

                return created;
            }
        }

        public async Task<IEnumerable<SellRequest>> GetSellRequestsForCampaignAsync(string campaignId)
        {
            var campaign = await _campaignRepository.GetByIdAsync(campaignId)
                ?? throw new KeyNotFoundException("Campaign not found.");

            var currentUserId = GetCurrentUserId();
            if (!campaign.OwnerIds.Contains(currentUserId))
            {
                throw new UnauthorizedAccessException("Only DMs can review sell requests.");
            }

            return await _sellRequestRepository.GetByCampaignIdAsync(campaignId);
        }

        public async Task<SellRequest?> ProcessSellRequestAsync(string requestId, bool approve)
        {
            var request = await _sellRequestRepository.GetByIdAsync(requestId)
                ?? throw new KeyNotFoundException("Sell request not found.");

            var campaign = await _campaignRepository.GetByIdAsync(request.CampaignId)
                ?? throw new KeyNotFoundException("Campaign not found.");

            var currentUserId = GetCurrentUserId();
            if (!campaign.OwnerIds.Contains(currentUserId))
            {
                throw new UnauthorizedAccessException("Only DMs can process requests.");
            }

            var targetStatus = approve ? SellRequestStatus.Approved : SellRequestStatus.Rejected;

            // Atomic state transition to prevent concurrent approval processing race conditions
            var updatedRequest = await _sellRequestRepository.TryUpdateStatusAsync(requestId, SellRequestStatus.Pending, targetStatus);
            if (updatedRequest == null)
            {
                throw new InvalidOperationException("This sell request has already been processed or completed.");
            }

            var character = await _characterRepository.GetByIdAsync(request.CharacterId)
                ?? throw new KeyNotFoundException("Character not found.");

            var shop = await _repository.GetByIdAsync(request.ShopId)
                ?? throw new KeyNotFoundException("Shop not found.");

            var shopInventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId)
                ?? throw new KeyNotFoundException("Shop stock register not found.");

            if (request.IsSteal)
            {
                if (approve)
                {
                    // APPROVED STEAL: Deduct item from Shop stock and move to player inventory (free of charge)
                    var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                    if (shopItem == null || (shopItem.Quantity ?? 0) < request.Quantity)
                    {
                        throw new InvalidOperationException("The shop no longer has sufficient stock of this item to steal.");
                    }

                    // Deduct from shop stock
                    shopItem.Quantity -= request.Quantity;
                    if (shopItem.Quantity <= 0)
                    {
                        shopInventory.Items.Remove(shopItem);
                    }
                    await _inventoryRepository.UpdateAsync(shopInventory);

                    // Add to player inventory bag
                    string playerInventoryId = character.InventoryIds?.FirstOrDefault()
                        ?? throw new InvalidOperationException("Character has no bag.");

                    var playerInventory = await _inventoryRepository.GetByIdAsync(playerInventoryId)
                        ?? throw new KeyNotFoundException("Player bag not found.");

                    var bagItem = playerInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                    if (bagItem != null)
                    {
                        bagItem.Quantity = (bagItem.Quantity ?? 0) + request.Quantity;
                    }
                    else
                    {
                        var equipment = await _equipmentRepository.GetByIdAsync(request.EquipmentId);
                        playerInventory.Items ??= new List<InventoryItem>();
                        playerInventory.Items.Add(new InventoryItem { EquipmentId = request.EquipmentId, EquipmentName = equipment?.Name, Quantity = request.Quantity });
                    }
                    await _inventoryRepository.UpdateAsync(playerInventory);

                    // Broadcast both updates
                    await BroadcastInventoryChangeAsync(shopInventory, "updated", shopInventory);
                    await BroadcastInventoryChangeAsync(playerInventory, "updated", playerInventory);
                }
                else
                {
                    // REJECTED STEAL: Nothing was deducted during request phase, so nothing to restore.
                    // Request status is already transitioned atomic-style to Rejected, which updates the view.
                }
            }
            else
            {
                // STANDARD SELL APPROVAL / REJECTION
                if (approve)
                {
                    // 1. Pay Player
                    int priceSp = (int)Math.Round(request.OfferedPriceGp * 100);
                    _currencyService.AddAndConsolidateCurrency(character, priceSp);
                    character = await _characterRepository.UpdateAsync(character) ?? character;

                    // 2. Deduct from Shop Register Till (DMs register can go negative!)
                    _currencyService.RemoveCurrencyFromInventory(shopInventory, priceSp, allowNegative: true);
                    await _inventoryRepository.UpdateAsync(shopInventory);

                    // 3. Move escrowed item into Shop Stock
                    var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                    if (shopItem != null)
                    {
                        shopItem.Quantity = (shopItem.Quantity ?? 0) + request.Quantity;
                    }
                    else
                    {
                        var equipment = await _equipmentRepository.GetByIdAsync(request.EquipmentId);
                        shopInventory.Items ??= new List<InventoryItem>();
                        shopInventory.Items.Add(new InventoryItem { EquipmentId = request.EquipmentId, EquipmentName = equipment?.Name, Quantity = request.Quantity });
                    }
                    await _inventoryRepository.UpdateAsync(shopInventory);

                    // Broadcast
                    await BroadcastCharacterChangeAsync(character, "updated", character);
                    await BroadcastInventoryChangeAsync(shopInventory, "updated", shopInventory);
                }
                else
                {
                    // Rejected: Return escrowed item to Player bag
                    string playerInventoryId = string.IsNullOrEmpty(request.SourceInventoryId) 
                        ? character.InventoryIds?.FirstOrDefault() ?? throw new InvalidOperationException("Character has no bag.")
                        : request.SourceInventoryId;

                    var playerInventory = await _inventoryRepository.GetByIdAsync(playerInventoryId)
                        ?? throw new KeyNotFoundException("Player bag not found.");

                    var bagItem = playerInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                    if (bagItem != null)
                    {
                        bagItem.Quantity = (bagItem.Quantity ?? 0) + request.Quantity;
                    }
                    else
                    {
                        var equipment = await _equipmentRepository.GetByIdAsync(request.EquipmentId);
                        playerInventory.Items ??= new List<InventoryItem>();
                        playerInventory.Items.Add(new InventoryItem { EquipmentId = request.EquipmentId, EquipmentName = equipment?.Name, Quantity = request.Quantity });
                    }
                    await _inventoryRepository.UpdateAsync(playerInventory);

                    // Broadcast
                    await BroadcastInventoryChangeAsync(playerInventory, "updated", playerInventory);
                }
            }

            return updatedRequest;
        }

        public int ConvertCostToSilver(Cost? cost)
        {
            if (cost == null) return 0;
            string unit = (cost.Unit ?? "gp").ToLower().Trim();
            int quantity = cost.Quantity;

            return unit switch
            {
                "gp" => quantity * 100,
                "sp" => quantity,
                _ => quantity * 100
            };
        }

        public async Task<IEnumerable<dndhelper.Models.DTOs.ShopItemResponse>> GetShopItemsAsync(string shopId)
        {
            var shop = await _repository.GetByIdAsync(shopId);
            if (shop == null || string.IsNullOrEmpty(shop.InventoryId)) return new List<dndhelper.Models.DTOs.ShopItemResponse>();

            var inventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId);
            if (inventory == null || inventory.Items == null || !inventory.Items.Any()) return new List<dndhelper.Models.DTOs.ShopItemResponse>();

            var equipmentIds = inventory.Items.Select(i => i.EquipmentId).Where(id => !string.IsNullOrEmpty(id)).ToList();
            var equipments = await _equipmentRepository.GetByIdsAsync(equipmentIds!);

            var responseList = new List<dndhelper.Models.DTOs.ShopItemResponse>();

            foreach (var item in inventory.Items)
            {
                var equipmentData = equipments.FirstOrDefault(e => e.Id == item.EquipmentId);
                if (equipmentData == null) continue;

                // Calculate cost in Silver Pieces (sp) using the shop's multiplier
                int baseCostSp = ConvertCostToSilver(equipmentData.Cost);
                
                int finalCostSp = (int)Math.Round(baseCostSp * shop.PriceMultiplier);
                string displayCost = finalCostSp % 100 == 0 ? $"{finalCostSp / 100} gp" : $"{finalCostSp} sp";

                responseList.Add(new dndhelper.Models.DTOs.ShopItemResponse
                {
                    EquipmentId = item.EquipmentId!,
                    EquipmentName = item.EquipmentName ?? equipmentData.Name,
                    Quantity = item.Quantity ?? 1,
                    Note = item.Note,
                    FinalCostSp = finalCostSp,
                    DisplayCost = displayCost,
                    Description = equipmentData.Description,
                    Damage = equipmentData.Damage,
                    Range = equipmentData.Range,
                    Weight = equipmentData.Weight,
                    Tags = equipmentData.Tags,
                    Tier = equipmentData.Tier
                });
            }

            return responseList;
        }

        #region Broadcast Helpers
        private async Task BroadcastCharacterChangeAsync(Character character, string action, object data)
        {
            try
            {
                await _entitySyncService.BroadcastEntityUpdated("Character", character.Id!, data, GetCurrentUserId());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to broadcast character update. CharacterId={CharacterId}", character.Id);
            }
        }

        private async Task BroadcastInventoryChangeAsync(Inventory inventory, string action, object data)
        {
            try
            {
                await _entitySyncService.BroadcastEntityUpdated("Inventory", inventory.Id!, data, GetCurrentUserId());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to broadcast inventory update. InventoryId={InventoryId}", inventory.Id);
            }
        }
        #endregion
    }
}
