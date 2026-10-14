using dndhelper.Models.CharacterModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace dndhelper.Utils
{
    /// <summary>
    /// Sheet values with the active effects applied, resolved like computeEffects in the frontend's utils/effects.ts
    /// (keep the two in step): "min" raises the floor and bonuses add up in order, then the last "set" wins, with
    /// only its own effect's bonuses stacked on top.
    /// </summary>
    public static class CharacterStats
    {
        public static int EffectiveAc(Character c) => Resolve(c, "ac", c.ArmorClass ?? 10);

        /// <summary>The sheet's max HP plus "maxHp" modifiers and the CON modifier change for every level.</summary>
        public static int EffectiveMaxHp(Character c)
        {
            var con = c.AbilityScores?.Con ?? 10;
            var conChange = AbilityMod(Resolve(c, "con", con)) - AbilityMod(con);
            return Resolve(c, "maxHp", (c.MaxHitPoints ?? 0) + conChange * (c.Level ?? 1));
        }

        private static int AbilityMod(int score) => (int)Math.Floor((score - 10) / 2.0);

        private static int Resolve(Character c, string target, int value)
        {
            var situations = new HashSet<string>((c.ActiveSituations ?? new List<string>()).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            var mods = (c.Effects ?? new List<CharacterEffect>())
                .Where(e => e.Active)
                .SelectMany(e => (e.Modifiers ?? new List<StatModifier>())
                    .Where(m => m.Target == target && m.Value != null
                        && (string.IsNullOrWhiteSpace(m.When) || situations.Contains(m.When.Trim())))
                    .Select(m => (Effect: e.Id, m.Type, Value: m.Value!.Value)))
                .ToList();

            foreach (var m in mods)
                if (m.Type == "min") value = Math.Max(value, m.Value);
                else if (m.Type == "bonus") value += m.Value;

            var set = mods.LastOrDefault(m => m.Type == "set");
            if (set.Type != null)
                value = set.Value + mods.Where(m => m.Type == "bonus" && m.Effect == set.Effect).Sum(m => m.Value);
            return value;
        }
    }
}
