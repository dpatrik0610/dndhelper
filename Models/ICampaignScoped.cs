namespace dndhelper.Models
{
    /// <summary>
    /// Entity that lives inside one campaign. Only members of that campaign (or the superadmin)
    /// may access it. A null CampaignId means "not assigned to a campaign": no campaign check
    /// applies and ownership alone decides.
    /// </summary>
    public interface ICampaignScoped
    {
        string? CampaignId { get; set; }
    }

    /// <summary>
    /// Campaign content (spells, items, monsters, rules). Members read, DMs write.
    /// A null CampaignId means core content: read-only, shared by every campaign that
    /// imported its type, editable only by the superadmin.
    /// </summary>
    public interface ICampaignContent : ICampaignScoped { }

    public static class CoreContentTypes
    {
        public const string Spells = "Spells";
        public const string Equipment = "Equipment";
        public const string Monsters = "Monsters";
        public const string Rules = "Rules";

        public static readonly string[] All = { Spells, Equipment, Monsters, Rules };

        public static string? Of(System.Type type) => type.Name switch
        {
            nameof(Spell) => Spells,
            nameof(Models.Equipment) => Equipment,
            nameof(Monster) => Monsters,
            nameof(RuleModels.Rule) => Rules,
            _ => null
        };
    }
}
