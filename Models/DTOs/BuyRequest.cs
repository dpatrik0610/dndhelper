namespace dndhelper.Models.DTOs
{
    public class BuyRequest
    {
        public string BuyerCharacterId { get; set; } = null!;
        public string EquipmentId { get; set; } = null!;
        public int Quantity { get; set; } = 1;
    }
}
