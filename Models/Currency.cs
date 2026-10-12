namespace dndhelper.Models
{
    /// <summary>
    /// A stack of one currency. Type is the coin's name: "gp", "sp", "cp", "pp", "ep",
    /// or any custom name ("Mammon"). Types are matched case-insensitively.
    /// </summary>
    public class Currency
    {
        public string Type { get; set; } = "";
        public int Amount { get; set; }
    }
}
