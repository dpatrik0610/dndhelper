using dndhelper.Models;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace dndhelper.Database.Migrations
{
    /// <summary>
    /// Turns a 5etools bestiary document (as the old import stored it) into the flat Monster model:
    /// resolves "_copy" references with their "_mod" edits, and renders {@tag} markup as plain text.
    /// Pure functions, no database access.
    /// </summary>
    public class FiveEToolsMonster
    {
        private static readonly string[] FeatureFields = { "trait", "action", "bonus", "reaction", "legendary", "mythic", "spellcasting" };
        private static readonly Dictionary<string, string> Abilities = new()
        {
            ["str"] = "Strength", ["dex"] = "Dexterity", ["con"] = "Constitution",
            ["int"] = "Intelligence", ["wis"] = "Wisdom", ["cha"] = "Charisma",
        };
        private static readonly Dictionary<string, string> Sizes = new()
        {
            ["T"] = "Tiny", ["S"] = "Small", ["M"] = "Medium", ["L"] = "Large", ["H"] = "Huge", ["G"] = "Gargantuan",
        };

        private readonly Dictionary<(string, string), BsonDocument> _byNameAndSource;

        public int CopiesResolved { get; private set; }
        public List<string> MissingParents { get; } = new();
        /// <summary>_mod modes (and _templates) that are not applied, with how often they were skipped.</summary>
        public Dictionary<string, int> Unsupported { get; } = new();

        public FiveEToolsMonster(IEnumerable<BsonDocument> all)
        {
            _byNameAndSource = new();
            foreach (var d in all)
                _byNameAndSource.TryAdd(Key(Str(d, "name"), Str(d, "source")), d);
        }

        private static (string, string) Key(string? name, string? source) =>
            ((name ?? "").ToLowerInvariant(), (source ?? "").ToLowerInvariant());

        public static bool IsLegacy(BsonDocument d) => !d.Contains(nameof(Monster.Name)) && d.Contains("name");

        public Monster Convert(BsonDocument raw)
        {
            var d = Resolve(raw, 0);
            var cr = Cr(d.GetValue("cr", BsonNull.Value));
            var name = Str(d, "name") ?? "Unnamed";
            var source = Str(d, "source");

            var m = new Monster
            {
                Id = S(raw["_id"]),
                CampaignId = raw.GetValue("CampaignId", BsonNull.Value) is { IsObjectId: true } c ? S(c) : null,
                IsDeleted = raw.GetValue("IsDeleted", false).ToBoolean(),
                CreatedAt = raw.GetValue("CreatedAt", BsonNull.Value) is { IsValidDateTime: true } at ? at.ToUniversalTime() : DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Name = Clean(name),
                Source = source,
                SourceKey = $"5etools/{name}|{source}".ToLowerInvariant(),
                IsNpc = d.GetValue("isNpc", false).ToBoolean(),
                Size = d.GetValue("size", BsonNull.Value) is BsonArray sizes
                    ? string.Join(" or ", sizes.Select(s => Sizes.GetValueOrDefault(S(s), S(s))))
                    : null,
                Alignment = Alignment(d),
                ChallengeRating = cr,
                AbilityScores = Scores(d.GetValue("abilityscores", BsonNull.Value)),
                PassivePerception = Passive(d.GetValue("passive", BsonNull.Value), cr),
            };

            MapType(d.GetValue("type", BsonNull.Value), m);
            MapArmor(d.GetValue("ac", BsonNull.Value), m);
            if (d.GetValue("hp", BsonNull.Value) is BsonDocument hp)
            {
                var special = Str(hp, "special") is string sp ? Clean(sp) : null;
                m.HitPoints = hp.GetValue("average", BsonNull.Value) is { IsNumeric: true } avg ? avg.ToInt32()
                    : int.TryParse(special, out var flat) ? flat : 0;
                m.HitDice = Str(hp, "formula") ?? (int.TryParse(special, out _) ? null : special);
            }
            MapSpeed(d.GetValue("speed", BsonNull.Value), m.Speed);

            m.SavingThrows = Bonuses(d.GetValue("save", BsonNull.Value));
            m.Skills = Bonuses(d.GetValue("skill", BsonNull.Value));
            m.DamageVulnerabilities = Defenses(d.GetValue("vulnerable", BsonNull.Value), "vulnerable");
            m.DamageResistances = Defenses(d.GetValue("resist", BsonNull.Value), "resist");
            m.DamageImmunities = Defenses(d.GetValue("immune", BsonNull.Value), "immune");
            m.ConditionImmunities = Defenses(d.GetValue("conditionImmune", BsonNull.Value), "conditionImmune");
            m.Senses = Strings(d.GetValue("senses", BsonNull.Value));
            m.Languages = Strings(d.GetValue("languages", BsonNull.Value));

            m.Traits = Features(d, "trait");
            m.Actions = Features(d, "action");
            m.BonusActions = Features(d, "bonus");
            m.Reactions = Features(d, "reaction");
            m.LegendaryActions = Features(d, "legendary");
            m.LegendaryActions.AddRange(Features(d, "mythic").Select(f => new MonsterFeature { Name = $"{f.Name} (Mythic)", Description = f.Description }));

            foreach (var sc in (d.GetValue("spellcasting", BsonNull.Value) as BsonArray ?? new BsonArray()).OfType<BsonDocument>())
            {
                var feature = Spellcasting(sc);
                (Str(sc, "displayAs") switch
                {
                    "action" => m.Actions,
                    "bonus" => m.BonusActions,
                    "reaction" => m.Reactions,
                    "legendary" => m.LegendaryActions,
                    _ => m.Traits,
                }).Add(feature);
            }

            return m;
        }

        // ---------- _copy ----------

        private BsonDocument Resolve(BsonDocument doc, int depth)
        {
            if (doc.GetValue("_copy", BsonNull.Value) is not BsonDocument copy || depth > 10)
                return doc;

            if (copy.Contains("_templates")) Skip("_templates");
            if (!_byNameAndSource.TryGetValue(Key(Str(copy, "name"), Str(copy, "source")), out var parent))
            {
                MissingParents.Add($"{Str(doc, "name")} ({Str(doc, "source")}) → {Str(copy, "name")} ({Str(copy, "source")})");
                return WithoutPlaceholders(doc);
            }

            var merged = (BsonDocument)Resolve(parent, depth + 1).DeepClone();
            foreach (var e in WithoutPlaceholders(doc))
                merged[e.Name] = e.Value;
            merged.Remove("_copy");

            if (copy.GetValue("_mod", BsonNull.Value) is BsonDocument mods)
                foreach (var mod in mods)
                    foreach (var op in mod.Value is BsonArray ops ? ops : new BsonArray { mod.Value })
                        ApplyMod(merged, mod.Name, op);

            if (depth == 0) CopiesResolved++;
            return merged;
        }

        /// <summary>The old import wrote cr -1 and ability scores of -1 on copies; those mean "take the parent's".</summary>
        private static BsonDocument WithoutPlaceholders(BsonDocument doc)
        {
            var d = (BsonDocument)doc.DeepClone();
            d.Remove("_copy");
            if (d.GetValue("cr", BsonNull.Value) is { IsNumeric: true } cr && cr.ToDouble() < 0) d.Remove("cr");
            if (d.GetValue("abilityscores", BsonNull.Value) is BsonDocument s && s.Values.Any(v => v.IsNumeric && v.ToInt32() < 0)) d.Remove("abilityscores");
            return d;
        }

        private void ApplyMod(BsonDocument m, string field, BsonValue op)
        {
            if (op.IsString)
            {
                if (op.AsString == "remove") m.Remove(field);
                else Skip(op.AsString);
                return;
            }
            if (op is not BsonDocument o) return;

            var mode = Str(o, "mode") ?? "";
            if (field == "*")
            {
                if (mode == "replaceTxt")
                    foreach (var f in FeatureFields.Where(m.Contains))
                        m[f] = ReplaceText(m[f], o);
                else Skip(mode);
                return;
            }

            var items = o.GetValue("items", BsonNull.Value) switch
            {
                BsonArray a => a.ToList(),
                BsonNull => new List<BsonValue>(),
                var single => new List<BsonValue> { single },
            };
            BsonArray Arr() => m.GetValue(field, BsonNull.Value) as BsonArray ?? (BsonArray)(m[field] = new BsonArray());
            bool Named(BsonValue item, string name) =>
                (item is BsonDocument doc ? Str(doc, "name") : item.IsString ? item.AsString : null)?.Equals(name, StringComparison.OrdinalIgnoreCase) == true;

            switch (mode)
            {
                case "replaceTxt":
                    if (m.Contains(field)) m[field] = ReplaceText(m[field], o);
                    break;
                case "appendArr":
                    Arr().AddRange(items);
                    break;
                case "prependArr":
                    { var a = Arr(); for (var i = items.Count - 1; i >= 0; i--) a.Insert(0, items[i]); }
                    break;
                case "insertArr":
                    { var a = Arr(); var at = Math.Clamp(o.GetValue("index", 0).ToInt32(), 0, a.Count); for (var i = items.Count - 1; i >= 0; i--) a.Insert(at, items[i]); }
                    break;
                case "appendIfNotExistsArr":
                    { var a = Arr(); foreach (var it in items.Where(it => !a.Contains(it))) a.Add(it); }
                    break;
                case "replaceArr":
                    {
                        var a = Arr();
                        var target = o.GetValue("replace", BsonNull.Value);
                        var idx = target is BsonDocument byIndex ? byIndex.GetValue("index", -1).ToInt32()
                            : target.IsString ? a.ToList().FindIndex(x => Named(x, target.AsString)) : -1;
                        if (idx < 0 || idx >= a.Count) break;
                        a.RemoveAt(idx);
                        for (var i = items.Count - 1; i >= 0; i--) a.Insert(idx, items[i]);
                    }
                    break;
                case "removeArr":
                    {
                        var a = Arr();
                        var names = o.GetValue("names", BsonNull.Value) switch
                        {
                            BsonArray n => n.Select(x => S(x)).ToList(),
                            BsonString n => new List<string> { n.AsString },
                            _ => items.Select(x => S(x)).ToList(),
                        };
                        foreach (var x in a.Where(x => names.Any(n => Named(x, n))).ToList()) a.Remove(x);
                    }
                    break;
                default:
                    Skip(mode); // addSkills, addSpells, replaceSpells, removeSpells, ...
                    break;
            }
        }

        /// <summary>5etools replaceTxt: a regex over rules text, never over names.</summary>
        private static BsonValue ReplaceText(BsonValue v, BsonDocument op)
        {
            var options = (Str(op, "flags") ?? "").Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None;
            Regex rx;
            try { rx = new Regex(Str(op, "replace") ?? "", options); }
            catch (ArgumentException) { return v; }
            var with = Str(op, "with") ?? "";

            BsonValue Walk(BsonValue x) => x switch
            {
                BsonString s => rx.Replace(s.AsString, with),
                BsonArray a => new BsonArray(a.Select(Walk)),
                BsonDocument doc => new BsonDocument(doc.Select(e => new BsonElement(e.Name, e.Name == "name" ? e.Value : Walk(e.Value)))),
                _ => x,
            };
            return Walk(v);
        }

        private void Skip(string what) => Unsupported[what] = Unsupported.GetValueOrDefault(what) + 1;

        // ---------- fields ----------

        private static void MapType(BsonValue type, Monster m)
        {
            if (type.IsString) { m.Type = type.AsString; return; }
            if (type is not BsonDocument t) return;

            m.Type = t.GetValue("type", BsonNull.Value) switch
            {
                BsonString s => s.AsString,
                BsonDocument choose when choose.GetValue("choose", BsonNull.Value) is BsonArray options => string.Join(" or ", options.Select(o => S(o))),
                _ => null,
            };
            m.Tags = (t.GetValue("tags", BsonNull.Value) as BsonArray ?? new BsonArray())
                .Select(tag => tag is BsonDocument td ? $"{Str(td, "prefix")} {Str(td, "tag")}".Trim() : S(tag))
                .ToList();
            if (t.Contains("swarmSize")) m.Tags.Insert(0, $"swarm of {Sizes.GetValueOrDefault(S(t["swarmSize"]), "")} creatures".Replace("  ", " "));
        }

        private static void MapArmor(BsonValue ac, Monster m)
        {
            if (ac is not BsonArray entries || entries.Count == 0) return;
            string Note(BsonDocument e) =>
                Str(e, "special") is string special ? Clean(special)
                : string.Join(" ", new[]
                {
                    e.GetValue("from", BsonNull.Value) is BsonArray from ? Clean(string.Join(", ", from.Select(f => S(f)))) : null,
                    Str(e, "condition") is string cond ? Clean(cond) : null,
                }.Where(s => !string.IsNullOrEmpty(s)));

            var notes = new List<string>();
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var value = e.IsNumeric ? e.ToInt32() : e is BsonDocument doc && doc.GetValue("ac", BsonNull.Value).IsNumeric ? doc["ac"].ToInt32() : (int?)null;
                var note = e is BsonDocument d ? Note(d) : "";
                if (i == 0)
                {
                    m.ArmorClass = value ?? 10;
                    if (note != "") notes.Add(note);
                }
                else notes.Add($"{value} {note}".Trim());
            }
            m.ArmorNote = notes.Count == 0 ? null : string.Join("; ", notes);
        }

        private static void MapSpeed(BsonValue speed, MonsterSpeed s)
        {
            if (speed is not BsonDocument d) return;
            int? Feet(string key) => d.GetValue(key, BsonNull.Value) switch
            {
                { IsNumeric: true } n => n.ToInt32(),
                BsonDocument withCondition when withCondition.GetValue("number", BsonNull.Value).IsNumeric => withCondition["number"].ToInt32(),
                _ => null,
            };
            s.Walk = Feet("walk") ?? 0;
            s.Fly = Feet("fly");
            s.Swim = Feet("swim");
            s.Climb = Feet("climb");
            s.Burrow = Feet("burrow");
            s.Hover = d.GetValue("canHover", false).ToBoolean();
        }

        private static AbilityScores Scores(BsonValue v)
        {
            int Get(string key) => v is BsonDocument d && d.GetValue(key, BsonNull.Value) is { IsNumeric: true } n && n.ToInt32() >= 0 ? n.ToInt32() : 10;
            return new AbilityScores { Str = Get("str"), Dex = Get("dex"), Con = Get("con"), Int = Get("int"), Wis = Get("wis"), Cha = Get("cha") };
        }

        private static double Cr(BsonValue v) => v switch
        {
            { IsNumeric: true } n => Math.Max(0, n.ToDouble()),
            BsonString s => Fraction(s.AsString),
            BsonDocument d => Cr(d.GetValue("cr", BsonNull.Value)),
            _ => 0,
        };

        private static double Fraction(string s)
        {
            var parts = s.Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b != 0)
                return a / b;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
        }

        public static int ProficiencyBonus(double cr) => cr < 5 ? 2 : (int)(cr - 1) / 4 + 2;

        /// <summary>A number, or a summon formula like "10 + (PB × 2)".</summary>
        private static int Passive(BsonValue v, double cr)
        {
            if (v.IsNumeric) return v.ToInt32();
            if (!v.IsString) return 10;
            var s = v.AsString;
            if (int.TryParse(s, out var n)) return n;
            var baseValue = Regex.Match(s, @"^\s*(\d+)") is { Success: true } b ? int.Parse(b.Groups[1].Value) : 10;
            if (!s.Contains("PB")) return baseValue;
            var times = Regex.Match(s, @"PB\s*[×x*]\s*(\d+)") is { Success: true } t ? int.Parse(t.Groups[1].Value) : 1;
            return baseValue + ProficiencyBonus(cr) * times;
        }

        private static Dictionary<string, int> Bonuses(BsonValue v)
        {
            var result = new Dictionary<string, int>();
            if (v is not BsonDocument d) return result;
            foreach (var e in d)
                if (e.Value.IsString && Regex.Match(e.Value.AsString, @"[+-]?\d+") is { Success: true } n)
                    result[e.Name] = int.Parse(n.Value);
            return result;
        }

        private static List<string> Defenses(BsonValue v, string key)
        {
            if (v is not BsonArray a) return new();
            return a.Select(item => item switch
            {
                BsonString s => Clean(s.AsString),
                BsonDocument d when Str(d, "special") is string special => Clean(special),
                BsonDocument d => string.Join(" ", new[]
                {
                    Str(d, "preNote") is string pre ? Clean(pre) : null,
                    JoinList(Defenses(d.GetValue(key, BsonNull.Value), key)),
                    Str(d, "note") is string note ? Clean(note) : null,
                }.Where(s => !string.IsNullOrEmpty(s))),
                _ => "",
            }).Where(s => s != "").ToList();
        }

        private static string JoinList(List<string> items) => items.Count switch
        {
            0 => "",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
        };

        private static List<string> Strings(BsonValue v) =>
            (v as BsonArray ?? new BsonArray()).Where(x => x.IsString).Select(x => Clean(x.AsString)).Where(s => s != "").ToList();

        private static string? Alignment(BsonDocument d)
        {
            if (d.GetValue("alignment", BsonNull.Value) is not BsonArray a || a.Count == 0) return null;
            string text;
            if (a.All(x => x.IsString))
                text = AlignmentWords(a.Select(x => x.AsString).ToList());
            else
                text = string.Join(" or ", a.OfType<BsonDocument>().Select(o =>
                    Str(o, "special") ?? (AlignmentWords((o.GetValue("alignment", BsonNull.Value) as BsonArray ?? new BsonArray()).Select(x => S(x)).ToList())
                        + (o.Contains("chance") ? $" ({o["chance"]}%)" : "")
                        + (Str(o, "note") is string note ? $" {note}" : ""))));
            return (Str(d, "alignmentPrefix") ?? "").ToLowerInvariant() + text;
        }

        private static string AlignmentWords(List<string> letters)
        {
            static string Word(string l) => l switch
            {
                "L" => "lawful", "C" => "chaotic", "G" => "good", "E" => "evil",
                "N" or "NX" or "NY" => "neutral", "U" => "unaligned", "A" => "any alignment", _ => l,
            };
            var set = letters.ToHashSet();
            string[] lawChaos = { "L", "NX", "C" }, goodEvil = { "G", "NY", "E" };
            var all = lawChaos.Concat(goodEvil).ToArray();

            if (set.Count == 1) return Word(letters[0]);
            if (set.Count >= 6 || all.All(set.Contains)) return "any alignment";
            if (set.Count == 5 && all.FirstOrDefault(x => !set.Contains(x)) is string missing) return $"any non-{Word(missing)} alignment";
            if (lawChaos.All(set.Contains) && set.Count == 4) return $"any {Word(set.First(x => !lawChaos.Contains(x)))} alignment";
            if (goodEvil.All(set.Contains) && set.Count == 4) return $"any {Word(set.First(x => !goodEvil.Contains(x)))} alignment";
            var words = letters.Select(Word).ToList();
            return words.Distinct().Count() == 1 ? words[0] : string.Join(" ", words);
        }

        // ---------- rules text ----------

        private static List<MonsterFeature> Features(BsonDocument d, string field) =>
            (d.GetValue(field, BsonNull.Value) as BsonArray ?? new BsonArray()).OfType<BsonDocument>()
                .Select(f => new MonsterFeature
                {
                    Name = Clean(Str(f, "name") ?? ""),
                    Description = Entries(f.GetValue("entries", BsonNull.Value)),
                })
                .Where(f => f.Name != "" || f.Description != "")
                .ToList();

        private static MonsterFeature Spellcasting(BsonDocument sc)
        {
            var lines = new List<string>();
            if (sc.Contains("headerEntries")) lines.Add(Entries(sc["headerEntries"]));

            string SpellList(BsonValue v) => string.Join(", ", (v as BsonArray ?? new BsonArray())
                .Where(s => !(s is BsonDocument h && h.GetValue("hidden", false).ToBoolean()))
                .Select(s => Clean(s is BsonDocument e ? Str(e, "entry") ?? "" : S(s))));

            if (sc.Contains("will")) lines.Add($"At will: {SpellList(sc["will"])}");
            foreach (var (field, per) in new[] { ("daily", "day"), ("rest", "rest"), ("restLong", "long rest"), ("weekly", "week"), ("monthly", "month"), ("yearly", "year") })
            {
                if (sc.GetValue(field, BsonNull.Value) is not BsonDocument freq) continue;
                foreach (var e in freq.OrderByDescending(e => e.Name))
                    lines.Add($"{e.Name.TrimEnd('e')}/{per}{(e.Name.EndsWith('e') ? " each" : "")}: {SpellList(e.Value)}");
            }
            if (sc.GetValue("spells", BsonNull.Value) is BsonDocument levels)
                foreach (var e in levels.OrderBy(e => e.Name))
                {
                    if (e.Value is not BsonDocument level) continue;
                    var label = e.Name == "0" ? "Cantrips (at will)"
                        : $"{Ordinal(e.Name)} level{(level.GetValue("slots", BsonNull.Value).IsNumeric ? $" ({level["slots"]} slots)" : "")}";
                    lines.Add($"{label}: {SpellList(level.GetValue("spells", BsonNull.Value))}");
                }
            if (sc.Contains("ritual")) lines.Add($"Rituals: {SpellList(sc["ritual"])}");
            if (sc.Contains("footerEntries")) lines.Add(Entries(sc["footerEntries"]));

            return new MonsterFeature
            {
                Name = Clean(Str(sc, "name") ?? "Spellcasting"),
                Description = string.Join("\n", lines.Where(l => l != "")),
            };
        }

        private static string Ordinal(string n) => n switch { "1" => "1st", "2" => "2nd", "3" => "3rd", _ => $"{n}th" };

        /// <summary>5etools entries (strings, lists, nested named entries, tables) as plain text lines.</summary>
        public static string Entries(BsonValue v) => v switch
        {
            BsonString s => Clean(s.AsString),
            BsonArray a => string.Join("\n", a.Select(Entries).Where(x => x != "")),
            BsonDocument d => Entry(d),
            _ => "",
        };

        private static string Entry(BsonDocument d)
        {
            var name = Str(d, "name") is string n ? Clean(n) : null;
            string Named(string body) => name == null ? body : body == "" ? name : $"{name}{(".:!?".Contains(name[^1]) ? "" : ".")} {body}";

            switch (Str(d, "type"))
            {
                case "list":
                    return string.Join("\n", (d.GetValue("items", BsonNull.Value) as BsonArray ?? new BsonArray()).Select(i => $"• {Entries(i)}"));
                case "item":
                case "itemSub":
                case "itemSpell":
                    return Named(Entries(d.GetValue("entry", BsonNull.Value) is BsonNull ? d.GetValue("entries", BsonNull.Value) : d["entry"]));
                case "table":
                    var rows = (d.GetValue("rows", BsonNull.Value) as BsonArray ?? new BsonArray())
                        .Select(r => string.Join(" | ", (r as BsonArray ?? new BsonArray()).Select(Entries)));
                    var header = d.GetValue("colLabels", BsonNull.Value) is BsonArray cols ? string.Join(" | ", cols.Select(Entries)) : null;
                    return string.Join("\n", new[] { Str(d, "caption") is string cap ? Clean(cap) : null, header }.Concat(rows).Where(x => !string.IsNullOrEmpty(x)));
                default:
                    return Named(Entries(d.GetValue("entries", BsonNull.Value) is BsonNull ? d.GetValue("entry", BsonNull.Value) : d["entries"]));
            }
        }

        private static readonly Regex Tag = new(@"\{@(\w+)(?:\s([^{}]*))?\}");
        // Tags whose first segment is the display text even with three or more segments.
        private static readonly HashSet<string> FirstSegment = new() { "filter", "quickref", "book", "adventure", "footnote", "note", "i", "b", "chance" };

        /// <summary>Renders {@tag ...} markup as the text a reader sees. Innermost tags first, so nesting works.</summary>
        public static string Clean(string s)
        {
            for (var i = 0; i < 10 && s.Contains("{@"); i++)
                s = Tag.Replace(s, m => TagText(m.Groups[1].Value, m.Groups[2].Value));
            return s.Trim();
        }

        private static string TagText(string tag, string text)
        {
            var parts = text.Split('|');
            var first = parts[0].Trim();
            switch (tag)
            {
                case "atk":
                    {
                        var kinds = first.Split(',').Select(k => k.Trim().StartsWith('m') ? "Melee" : "Ranged").Distinct();
                        return $"{string.Join(" or ", kinds)} {(first.Contains('s') ? "Spell" : "Weapon")} Attack:";
                    }
                case "atkr":
                    return $"{string.Join(" or ", first.Split(',').Select(k => k.Trim().StartsWith('m') ? "Melee" : "Ranged").Distinct())} Attack Roll:";
                case "hit":
                case "d20":
                    return first.StartsWith('-') || first.StartsWith('+') ? first : $"+{first}";
                case "h": return "Hit: ";
                case "m": return "Miss: ";
                case "hom": return "Hit or Miss: ";
                case "dc": return $"DC {first}";
                case "recharge": return first == "" ? "(Recharge 6)" : $"(Recharge {first}–6)";
                case "chance": return $"{first} percent";
                case "actSave": return $"{Abilities.GetValueOrDefault(first, first)} Saving Throw:";
                case "actSaveFail": return "Failure:";
                case "actSaveSuccess": return "Success:";
                case "actSaveSuccessOrFail": return "Failure or Success:";
                case "actTrigger": return "Trigger:";
                case "actResponse": return "Response:";
                case "hitYourSpellAttack": return first == "" ? "your spell attack modifier" : first;
            }
            if (FirstSegment.Contains(tag)) return first;
            return parts.Length >= 3 && parts[2].Trim() != "" ? parts[2].Trim() : first;
        }

        private static string S(BsonValue v) => v.ToString() ?? "";

        private static string? Str(BsonDocument d, string key) =>
            d.GetValue(key, BsonNull.Value) is { IsString: true } v ? v.AsString : null;
    }
}
