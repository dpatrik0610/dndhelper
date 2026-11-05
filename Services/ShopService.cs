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
                    new Currency { Type = "gp", Amount = 100 },
                    new Currency { Type = "sp", Amount = 100 }
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

            await CompletePurchaseAsync(shop, character, equipmentId, quantity, null, null);
            return true;
        }

        private async Task CompletePurchaseAsync(Shop shop, Character character, string equipmentId, int quantity, int? agreedCostSp, string? destinationInventoryId)
        {
            if (quantity < 1) throw new InvalidOperationException("Quantity must be at least 1.");

            var shopInventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId) ?? throw new KeyNotFoundException("Shop inventory not found.");
            var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == equipmentId) ?? throw new KeyNotFoundException("Item not sold here.");

            if (shopItem.Quantity.HasValue && shopItem.Quantity.Value < quantity)
            {
                throw new InvalidOperationException("Insufficient shop stock.");
            }

            var equipment = await _equipmentRepository.GetByIdAsync(equipmentId) ?? throw new KeyNotFoundException("Equipment specifications not found.");

            int totalCostSp = agreedCostSp ?? (int)Math.Round(ConvertCostToSilver(equipment.Cost) * shop.PriceMultiplier * quantity);

            _currencyService.ValidateCharacterHasFunds(character, totalCostSp);

            _currencyService.DeductAndConsolidateCurrency(character, totalCostSp);
            character = await _characterRepository.UpdateAsync(character) ?? character;

            _currencyService.AddCurrencyToInventory(shopInventory, totalCostSp);

            if (shopItem.Quantity.HasValue) 
            {
                shopItem.Quantity -= quantity;
                if (shopItem.Quantity.Value <= 0)
                {
                    shopInventory.Items?.Remove(shopItem);
                }
            }
            await _inventoryRepository.UpdateAsync(shopInventory);

            string playerInventoryId = destinationInventoryId ?? character.InventoryIds?.FirstOrDefault() ?? throw new InvalidOperationException("Character has no bag.");
            var playerInventory = await _inventoryRepository.GetByIdAsync(playerInventoryId) ?? throw new KeyNotFoundException("Player bag not found.");

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

            await BroadcastCharacterChangeAsync(character, "updated", character);
            await BroadcastInventoryChangeAsync(shopInventory, "updated", shopInventory);
            await BroadcastInventoryChangeAsync(playerInventory, "updated", playerInventory);
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
            request.CounterPriceGp = null;

            if (new[] { request.IsSteal, request.IsHaggle, request.IsTrade }.Count(kind => kind) > 1)
                throw new InvalidOperationException("A request can only be one kind of deal.");

            if (request.IsTrade) return await SubmitTradeAsync(request, character);

            request.GiveItems = new List<TradeItem>();
            request.WantItems = new List<TradeItem>();
            if (string.IsNullOrEmpty(request.EquipmentId)) throw new InvalidOperationException("No item was chosen.");
            if (request.Quantity < 1) throw new InvalidOperationException("Quantity must be at least 1.");
            if (request.OfferedPriceGp < 0) throw new InvalidOperationException("Offered price cannot be negative.");

            if (request.IsHaggle)
            {
                var shop = await _repository.GetByIdAsync(request.ShopId)
                    ?? throw new KeyNotFoundException("Shop not found.");
                if (!shop.IsOpened) throw new InvalidOperationException("The shop is closed.");

                var shopInventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId)
                    ?? throw new KeyNotFoundException("Shop stock register not found.");

                var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId)
                    ?? throw new KeyNotFoundException("Item not sold here.");
                if (shopItem.Quantity.HasValue && shopItem.Quantity.Value < request.Quantity)
                {
                    throw new InvalidOperationException("Insufficient shop stock.");
                }

                if (!string.IsNullOrEmpty(request.SourceInventoryId) &&
                    (character.InventoryIds == null || !character.InventoryIds.Contains(request.SourceInventoryId)))
                {
                    throw new UnauthorizedAccessException("Character does not own the specified inventory.");
                }

                _currencyService.ValidateCharacterHasFunds(character, ToSilver(request.OfferedPriceGp));

                return await _sellRequestRepository.CreateAsync(request);
            }

            if (request.IsSteal)
            {
                var shop = await _repository.GetByIdAsync(request.ShopId)
                    ?? throw new KeyNotFoundException("Shop not found.");

                var shopInventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId)
                    ?? throw new KeyNotFoundException("Shop stock register not found.");

                var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                if (shopItem == null || (shopItem.Quantity ?? 0) < request.Quantity)
                {
                    throw new InvalidOperationException("The shop has insufficient stock of this item to steal.");
                }

                request.OfferedPriceGp = 0;

                var created = await _sellRequestRepository.CreateAsync(request);

                return created;
            }
            else
            {
                string playerInventoryId;
                Inventory playerInventory;
                InventoryItem? bagItem = null;

                if (!string.IsNullOrEmpty(request.SourceInventoryId))
                {
                    if (character.InventoryIds == null || !character.InventoryIds.Contains(request.SourceInventoryId))
                    {
                        throw new UnauthorizedAccessException("Character does not own the specified source inventory.");
                    }

                    playerInventoryId = request.SourceInventoryId;
                    playerInventory = await _inventoryRepository.GetByIdAsync(playerInventoryId)
                        ?? throw new KeyNotFoundException("Player inventory bag not found.");

                    bagItem = playerInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                }
                else
                {
                    playerInventoryId = "";
                    playerInventory = null!;

                    if (character.InventoryIds != null)
                    {
                        foreach (var invId in character.InventoryIds)
                        {
                            var inv = await _inventoryRepository.GetByIdAsync(invId);
                            if (inv != null)
                            {
                                var item = inv.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                                if (item != null && (item.Quantity ?? 0) >= request.Quantity)
                                {
                                    playerInventoryId = invId;
                                    playerInventory = inv;
                                    bagItem = item;
                                    break;
                                }
                            }
                        }
                    }

                    // Fallback to character's first inventory if none has the item (to throw appropriate stock exception)
                    if (bagItem == null)
                    {
                        playerInventoryId = character.InventoryIds?.FirstOrDefault()
                            ?? throw new InvalidOperationException("Character has no inventory bag.");

                        playerInventory = await _inventoryRepository.GetByIdAsync(playerInventoryId)
                            ?? throw new KeyNotFoundException("Player inventory bag not found.");

                        bagItem = playerInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                    }

                    request.SourceInventoryId = playerInventoryId;
                }

                if (bagItem == null || (bagItem.Quantity ?? 0) < request.Quantity)
                {
                    throw new InvalidOperationException("Character has insufficient item stock to sell.");
                }

                bagItem.Quantity -= request.Quantity;
                if (bagItem.Quantity <= 0) 
                {
                    playerInventory.Items?.Remove(bagItem);
                }
                
                await _inventoryRepository.UpdateAsync(playerInventory);
                var created = await _sellRequestRepository.CreateAsync(request);

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

        public async Task<IEnumerable<SellRequest>> GetSellRequestsForCharacterAsync(string characterId)
        {
            var character = await _characterRepository.GetByIdAsync(characterId)
                ?? throw new KeyNotFoundException("Character not found.");
            await EnsureOwnershipAccess(character);

            return await _sellRequestRepository.GetByCharacterIdAsync(characterId);
        }

        public async Task<SellRequest?> ProcessSellRequestAsync(string requestId, bool approve)
        {
            var request = await GetRequestAsDmAsync(requestId);
            return await ResolveRequestAsync(request, approve, SellRequestStatus.Pending);
        }

        public async Task<SellRequest> CounterSellRequestAsync(string requestId, double counterPriceGp)
        {
            var request = await GetRequestAsDmAsync(requestId);
            if (request.IsSteal) throw new InvalidOperationException("Steal attempts cannot be countered.");
            if (counterPriceGp < 0 && !request.IsTrade) throw new InvalidOperationException("Counter price cannot be negative.");

            return await _sellRequestRepository.TryUpdateStatusAsync(requestId, SellRequestStatus.Pending, SellRequestStatus.Countered, counterPriceGp)
                ?? throw new InvalidOperationException("This request has already been processed or countered.");
        }

        public async Task<SellRequest?> RespondToCounterOfferAsync(string requestId, bool accept)
        {
            var request = await _sellRequestRepository.GetByIdAsync(requestId)
                ?? throw new KeyNotFoundException("Sell request not found.");

            var character = await _characterRepository.GetByIdAsync(request.CharacterId)
                ?? throw new KeyNotFoundException("Character not found.");
            await EnsureOwnershipAccess(character);

            return await ResolveRequestAsync(request, accept, SellRequestStatus.Countered);
        }

        private async Task<SellRequest> GetRequestAsDmAsync(string requestId)
        {
            var request = await _sellRequestRepository.GetByIdAsync(requestId)
                ?? throw new KeyNotFoundException("Sell request not found.");

            var campaign = await _campaignRepository.GetByIdAsync(request.CampaignId)
                ?? throw new KeyNotFoundException("Campaign not found.");

            if (!campaign.OwnerIds.Contains(GetCurrentUserId()))
            {
                throw new UnauthorizedAccessException("Only DMs can process requests.");
            }

            return request;
        }

        private async Task<SellRequest?> ResolveRequestAsync(SellRequest request, bool approve, SellRequestStatus fromStatus)
        {
            var requestId = request.Id!;
            var targetStatus = approve ? SellRequestStatus.Approved : SellRequestStatus.Rejected;

            // Atomic state transition to prevent concurrent approval processing race conditions
            var updatedRequest = await _sellRequestRepository.TryUpdateStatusAsync(requestId, fromStatus, targetStatus);
            if (updatedRequest == null)
            {
                throw new InvalidOperationException("This sell request has already been processed or completed.");
            }

            double agreedPriceGp = updatedRequest.CounterPriceGp ?? updatedRequest.OfferedPriceGp;

            var character = await _characterRepository.GetByIdAsync(request.CharacterId)
                ?? throw new KeyNotFoundException("Character not found.");

            var shop = await _repository.GetByIdAsync(request.ShopId)
                ?? throw new KeyNotFoundException("Shop not found.");

            var shopInventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId)
                ?? throw new KeyNotFoundException("Shop stock register not found.");

            if (request.IsTrade)
            {
                try
                {
                    await ResolveTradeAsync(request, approve, character, shopInventory, agreedPriceGp);
                }
                catch
                {
                    await _sellRequestRepository.TryUpdateStatusAsync(requestId, targetStatus, fromStatus);
                    throw;
                }
                return updatedRequest;
            }

            if (request.IsHaggle)
            {
                if (approve)
                {
                    try
                    {
                        await CompletePurchaseAsync(shop, character, request.EquipmentId!,request.Quantity, ToSilver(agreedPriceGp), request.SourceInventoryId);
                    }
                    catch
                    {
                        await _sellRequestRepository.TryUpdateStatusAsync(requestId, targetStatus, fromStatus);
                        throw;
                    }
                }
                return updatedRequest;
            }

            if (request.IsSteal)
            {
                if (approve)
                {
                    var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == request.EquipmentId);
                    if (shopItem == null || (shopItem.Quantity ?? 0) < request.Quantity)
                    {
                        throw new InvalidOperationException("The shop no longer has sufficient stock of this item to steal.");
                    }

                    shopItem.Quantity -= request.Quantity;
                    if (shopItem.Quantity <= 0)
                    {
                        shopInventory.Items.Remove(shopItem);
                    }
                    await _inventoryRepository.UpdateAsync(shopInventory);

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
                if (approve)
                {
                    int priceSp = ToSilver(agreedPriceGp);
                    _currencyService.AddAndConsolidateCurrency(character, priceSp);
                    character = await _characterRepository.UpdateAsync(character) ?? character;

                    // The shop register may go negative.
                    _currencyService.RemoveCurrencyFromInventory(shopInventory, priceSp, allowNegative: true);
                    await _inventoryRepository.UpdateAsync(shopInventory);

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

                    await BroadcastCharacterChangeAsync(character, "updated", character);
                    await BroadcastInventoryChangeAsync(shopInventory, "updated", shopInventory);
                }
                else
                {
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

                    await BroadcastInventoryChangeAsync(playerInventory, "updated", playerInventory);
                }
            }

            return updatedRequest;
        }

        private static int ToSilver(double gp) => (int)Math.Round(gp * 100);

        private async Task<SellRequest> SubmitTradeAsync(SellRequest request, Character character)
        {
            request.EquipmentId = null;
            request.GiveItems ??= new List<TradeItem>();
            request.WantItems ??= new List<TradeItem>();

            var lines = request.GiveItems.Concat(request.WantItems).ToList();
            if (lines.Count == 0) throw new InvalidOperationException("An offer needs at least one item.");
            if (lines.Any(l => l.Quantity < 1 || string.IsNullOrEmpty(l.EquipmentId)))
                throw new InvalidOperationException("Every item in an offer needs a quantity of at least 1.");
            if (request.WantItems.GroupBy(i => i.EquipmentId).Any(g => g.Count() > 1) ||
                request.GiveItems.GroupBy(i => (i.EquipmentId, i.SourceInventoryId)).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Each item can only appear once on each side of an offer.");

            if (!string.IsNullOrEmpty(request.SourceInventoryId) && character.InventoryIds?.Contains(request.SourceInventoryId) != true)
                throw new UnauthorizedAccessException("Character does not own the specified inventory.");

            var shop = await _repository.GetByIdAsync(request.ShopId) ?? throw new KeyNotFoundException("Shop not found.");
            if (!shop.IsOpened) throw new InvalidOperationException("The shop is closed.");
            var shopInventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId)
                ?? throw new KeyNotFoundException("Shop stock register not found.");

            foreach (var want in request.WantItems)
            {
                var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == want.EquipmentId)
                    ?? throw new KeyNotFoundException("An item you asked for is not sold here.");
                if (shopItem.Quantity.HasValue && shopItem.Quantity.Value < want.Quantity)
                    throw new InvalidOperationException($"The shop does not have {want.Quantity}× {shopItem.EquipmentName}.");
                want.EquipmentName = shopItem.EquipmentName;
                want.SourceInventoryId = null;
            }

            var bags = new Dictionary<string, Inventory>();
            foreach (var give in request.GiveItems)
            {
                if (string.IsNullOrEmpty(give.SourceInventoryId) || character.InventoryIds?.Contains(give.SourceInventoryId) != true)
                    throw new UnauthorizedAccessException("Character does not own the inventory an offered item comes from.");

                if (!bags.TryGetValue(give.SourceInventoryId, out var bag))
                {
                    bag = await _inventoryRepository.GetByIdAsync(give.SourceInventoryId)
                        ?? throw new KeyNotFoundException("Player inventory bag not found.");
                    bags[give.SourceInventoryId] = bag;
                }

                var bagItem = bag.Items?.FirstOrDefault(i => i.EquipmentId == give.EquipmentId);
                if (bagItem == null || (bagItem.Quantity ?? 0) < give.Quantity)
                    throw new InvalidOperationException("You do not have enough of an item you offered.");
                give.EquipmentName = bagItem.EquipmentName;
            }

            if (request.OfferedPriceGp > 0) _currencyService.ValidateCharacterHasFunds(character, ToSilver(request.OfferedPriceGp));

            foreach (var give in request.GiveItems) TakeItem(bags[give.SourceInventoryId!], give.EquipmentId, give.Quantity);
            foreach (var bag in bags.Values) await _inventoryRepository.UpdateAsync(bag);

            var created = await _sellRequestRepository.CreateAsync(request);
            foreach (var bag in bags.Values) await BroadcastInventoryChangeAsync(bag, "updated", bag);
            return created;
        }

        private async Task ResolveTradeAsync(SellRequest request, bool approve, Character character, Inventory shopInventory, double priceGp)
        {
            if (!approve)
            {
                var bags = new Dictionary<string, Inventory>();
                foreach (var give in request.GiveItems)
                {
                    var bagId = give.SourceInventoryId!;
                    if (!bags.TryGetValue(bagId, out var bag))
                    {
                        bag = await _inventoryRepository.GetByIdAsync(bagId) ?? throw new KeyNotFoundException("Player bag not found.");
                        bags[bagId] = bag;
                    }
                    PutItem(bag, give.EquipmentId, give.EquipmentName, give.Quantity);
                }
                foreach (var bag in bags.Values)
                {
                    await _inventoryRepository.UpdateAsync(bag);
                    await BroadcastInventoryChangeAsync(bag, "updated", bag);
                }
                return;
            }

            foreach (var want in request.WantItems)
            {
                var shopItem = shopInventory.Items?.FirstOrDefault(i => i.EquipmentId == want.EquipmentId);
                if (shopItem == null || (shopItem.Quantity.HasValue && shopItem.Quantity.Value < want.Quantity))
                    throw new InvalidOperationException($"The shop no longer has {want.Quantity}× {want.EquipmentName ?? "of a requested item"}.");
            }

            int priceSp = ToSilver(priceGp);
            if (priceSp > 0) _currencyService.ValidateCharacterHasFunds(character, priceSp);

            string playerInventoryId = request.SourceInventoryId ?? character.InventoryIds?.FirstOrDefault()
                ?? throw new InvalidOperationException("Character has no bag.");
            var playerInventory = await _inventoryRepository.GetByIdAsync(playerInventoryId)
                ?? throw new KeyNotFoundException("Player bag not found.");

            foreach (var want in request.WantItems)
            {
                TakeItem(shopInventory, want.EquipmentId, want.Quantity);
                PutItem(playerInventory, want.EquipmentId, want.EquipmentName, want.Quantity);
            }
            foreach (var give in request.GiveItems) PutItem(shopInventory, give.EquipmentId, give.EquipmentName, give.Quantity);

            if (priceSp > 0)
            {
                _currencyService.DeductAndConsolidateCurrency(character, priceSp);
                _currencyService.AddCurrencyToInventory(shopInventory, priceSp);
            }
            else if (priceSp < 0)
            {
                _currencyService.AddAndConsolidateCurrency(character, -priceSp);
                _currencyService.RemoveCurrencyFromInventory(shopInventory, -priceSp, allowNegative: true);
            }
            if (priceSp != 0) character = await _characterRepository.UpdateAsync(character) ?? character;

            await _inventoryRepository.UpdateAsync(shopInventory);
            await _inventoryRepository.UpdateAsync(playerInventory);

            if (priceSp != 0) await BroadcastCharacterChangeAsync(character, "updated", character);
            await BroadcastInventoryChangeAsync(shopInventory, "updated", shopInventory);
            await BroadcastInventoryChangeAsync(playerInventory, "updated", playerInventory);
        }

        private static void TakeItem(Inventory inventory, string equipmentId, int quantity)
        {
            var item = inventory.Items?.FirstOrDefault(i => i.EquipmentId == equipmentId);
            if (item?.Quantity == null) return;
            item.Quantity -= quantity;
            if (item.Quantity <= 0) inventory.Items!.Remove(item);
        }

        private static void PutItem(Inventory inventory, string equipmentId, string? name, int quantity)
        {
            var item = inventory.Items?.FirstOrDefault(i => i.EquipmentId == equipmentId);
            if (item != null)
            {
                if (item.Quantity.HasValue) item.Quantity += quantity;
                return;
            }
            inventory.Items ??= new List<InventoryItem>();
            inventory.Items.Add(new InventoryItem { EquipmentId = equipmentId, EquipmentName = name, Quantity = quantity });
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
            await Access.EnsureMemberAsync(shop.CampaignId);

            var inventory = await _inventoryRepository.GetByIdAsync(shop.InventoryId);
            if (inventory == null || inventory.Items == null || !inventory.Items.Any()) return new List<dndhelper.Models.DTOs.ShopItemResponse>();

            var equipmentIds = inventory.Items.Select(i => i.EquipmentId).Where(id => !string.IsNullOrEmpty(id)).ToList();
            var equipments = await _equipmentRepository.GetByIdsAsync(equipmentIds!);

            var responseList = new List<dndhelper.Models.DTOs.ShopItemResponse>();

            foreach (var item in inventory.Items)
            {
                var equipmentData = equipments.FirstOrDefault(e => e.Id == item.EquipmentId);
                if (equipmentData == null) continue;

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
                var changedBy = _user?.Identity?.Name ?? GetCurrentUserId() ?? "System";
                await _entitySyncService.BroadcastEntityUpdated("Character", character.Id!, data, changedBy);
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
                var changedBy = _user?.Identity?.Name ?? GetCurrentUserId() ?? "System";
                await _entitySyncService.BroadcastEntityUpdated("Inventory", inventory.Id!, data, changedBy);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to broadcast inventory update. InventoryId={InventoryId}", inventory.Id);
            }
        }
        #endregion
    }
}
