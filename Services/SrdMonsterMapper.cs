using dndhelper.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace dndhelper.Services
{
    /// <summary>dnd5eapi.co monster JSON (SRD 5.1 "2014" and SRD 5.2 "2024" share the shape) to a Monster.</summary>
    public static class SrdMonsterMapper
    {
        public static readonly Dictionary<string, string> Rulesets = new()
        {
            ["2014"] = "SRD 5.1",
            ["2024"] = "SRD 5.2",
        };

        public static Monster Map(JObject j, string ruleset)
        {
            var m = new Monster
            {
                Name = (string?)j["name"] ?? "",
                Source = Rulesets[ruleset],
                SourceKey = $"srd-{ruleset}/{(string?)j["index"]}",
                Size = (string?)j["size"],
                Type = Lower((string?)j["type"]),
                Alignment = (string?)j["alignment"],
                HitPoints = (int?)j["hit_points"] ?? 0,
                HitDice = (string?)j["hit_points_roll"] ?? (string?)j["hit_dice"],
                ChallengeRating = (double?)j["challenge_rating"] ?? 0,
                Description = (string?)j["desc"],
                AbilityScores = new AbilityScores
                {
                    Str = (int?)j["strength"] ?? 10,
                    Dex = (int?)j["dexterity"] ?? 10,
                    Con = (int?)j["constitution"] ?? 10,
                    Int = (int?)j["intelligence"] ?? 10,
                    Wis = (int?)j["wisdom"] ?? 10,
                    Cha = (int?)j["charisma"] ?? 10,
                },
            };

            if (j["subtype"] is JValue { Type: JTokenType.String } subtype)
                m.Tags = ((string)subtype!).Split(',').Select(t => t.Trim()).Where(t => t != "").ToList();

            MapArmor(j["armor_class"] as JArray, m);
            MapSpeed(j["speed"] as JObject, m.Speed);

            foreach (var p in j["proficiencies"] as JArray ?? new JArray())
            {
                var index = (string?)p["proficiency"]?["index"] ?? "";
                var value = (int?)p["value"] ?? 0;
                if (index.StartsWith("saving-throw-")) m.SavingThrows[index["saving-throw-".Length..]] = value;
                else if (index.StartsWith("skill-")) m.Skills[index["skill-".Length..].Replace('-', ' ')] = value;
            }

            m.DamageVulnerabilities = Strings(j["damage_vulnerabilities"]);
            m.DamageResistances = Strings(j["damage_resistances"]);
            m.DamageImmunities = Strings(j["damage_immunities"]);
            m.ConditionImmunities = (j["condition_immunities"] as JArray ?? new JArray())
                .Select(c => Lower((string?)c["name"]) ?? "").Where(c => c != "").ToList();

            if (j["senses"] is JObject senses)
                foreach (var (key, value) in senses)
                {
                    if (key == "passive_perception") m.PassivePerception = (int?)value ?? 10;
                    else m.Senses.Add($"{key.Replace('_', ' ')} {value}");
                }

            var languages = (string?)j["languages"] ?? "";
            m.Languages = languages.Split(", ").Select(l => l.Trim()).Where(l => l is not ("" or "-" or "—")).ToList();

            m.Traits = Features(j["special_abilities"]);
            m.Actions = Features(j["actions"]);
            m.BonusActions = Features(j["bonus_actions"]);
            m.Reactions = Features(j["reactions"]);
            m.LegendaryActions = Features(j["legendary_actions"]);
            return m;
        }

        private static void MapArmor(JArray? entries, Monster m)
        {
            if (entries == null || entries.Count == 0) return;
            string? Note(JToken e) => (string?)e["type"] switch
            {
                "natural" => "natural armor",
                "armor" => string.Join(", ", (e["armor"] as JArray ?? new JArray()).Select(a => Lower((string?)a["name"]))),
                "spell" => Lower((string?)e["spell"]?["name"]),
                "condition" => (string?)e["desc"],
                _ => null,
            };

            m.ArmorClass = (int?)entries[0]["value"] ?? 10;
            var notes = new List<string>();
            if (Note(entries[0]) is { Length: > 0 } first) notes.Add(first);
            foreach (var e in entries.Skip(1))
                notes.Add($"{(int?)e["value"]} {Note(e)}".Trim());
            m.ArmorNote = notes.Count == 0 ? null : string.Join("; ", notes);
        }

        private static void MapSpeed(JObject? speed, MonsterSpeed s)
        {
            if (speed == null) return;
            int? Feet(string key) => speed[key] is JToken t && Regex.Match(t.ToString(), @"\d+") is { Success: true } d ? int.Parse(d.Value) : null;
            s.Walk = Feet("walk") ?? 0;
            s.Fly = Feet("fly");
            s.Swim = Feet("swim");
            s.Climb = Feet("climb");
            s.Burrow = Feet("burrow");
            s.Hover = (bool?)speed["hover"] ?? false;
        }

        private static List<MonsterFeature> Features(JToken? list) =>
            (list as JArray ?? new JArray()).Select(f =>
            {
                var name = (string?)f["name"] ?? "";
                return new MonsterFeature
                {
                    // Some entries already carry "(3/Day)" in the name.
                    Name = name.Contains('(') ? name : name + Usage(f["usage"]),
                    Description = (string?)f["desc"] ?? "",
                };
            }).ToList();

        /// <summary>"(Recharge 5–6)", "(3/Day)"; dnd5eapi keeps these out of the name.</summary>
        private static string Usage(JToken? usage) => (string?)usage?["type"] switch
        {
            "recharge on roll" => (int?)usage!["min_value"] is int min && min < 6 ? $" (Recharge {min}–6)" : " (Recharge 6)",
            "per day" => $" ({(int?)usage!["times"]}/Day)",
            "recharge after rest" => " (Recharges after a Short or Long Rest)",
            _ => "",
        };

        private static List<string> Strings(JToken? list) =>
            (list as JArray ?? new JArray()).Select(t => t.ToString()).Where(t => t != "").ToList();

        private static string? Lower(string? s) => s?.ToLowerInvariant();
    }
}
