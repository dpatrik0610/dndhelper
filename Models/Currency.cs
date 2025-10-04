namespace dndhelper.Models
{
    public class Currency
    {
        public string? Type { get; set; }
        public int? Amount { get; set; }
        public string? CurrencyCode { get; set; } = null;
    }
}
