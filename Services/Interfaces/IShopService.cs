using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IShopService : IBaseService<Shop>
    {
        Task<IEnumerable<Shop>> GetShopsForCampaignAsync(string campaignId);
        Task<Shop?> ToggleShopOpenStatusAsync(string shopId, bool isOpened);
        Task<Shop?> CreateShopWithInventoryAsync(Shop shop);
        Task<IEnumerable<dndhelper.Models.DTOs.ShopItemResponse>> GetShopItemsAsync(string shopId);
        
        // Buy Item Pipeline
        Task<bool> BuyItemFromShopAsync(string shopId, string buyerCharacterId, string equipmentId, int quantity);
        
        // Sell Request Pipeline
        Task<SellRequest> SubmitSellRequestAsync(SellRequest request);
        Task<IEnumerable<SellRequest>> GetSellRequestsForCampaignAsync(string campaignId);
        Task<SellRequest?> ProcessSellRequestAsync(string requestId, bool approve);
    }
}
