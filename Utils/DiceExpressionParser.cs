using System;
using System.Text.RegularExpressions;

namespace dndhelper.Utils
{
    public static class DiceExpressionParser
    {
        private static readonly Regex ExpressionRegex = new Regex(
            @"^\s*(?<count>\d*)d(?<sides>\d+)\s*(?<modsign>[+-])?\s*(?<mod>\d+)?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static (int NumberOfDice, int Sides, int Modifier, string Normalized) Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new FormatException("Dice expression cannot be empty.");

            var match = ExpressionRegex.Match(expression);
            if (!match.Success)
                throw new FormatException("Invalid dice expression format.");

            var countText = match.Groups["count"].Value;
            var sidesText = match.Groups["sides"].Value;
            var signText = match.Groups["modsign"].Value;
            var modText = match.Groups["mod"].Value;

            var numberOfDice = string.IsNullOrWhiteSpace(countText) ? 1 : int.Parse(countText);
            var sides = int.Parse(sidesText);
            var modifier = 0;

            if (!string.IsNullOrWhiteSpace(modText))
            {
                var raw = int.Parse(modText);
                modifier = signText == "-" ? -raw : raw;
            }

            var normalized = BuildNormalized(numberOfDice, sides, modifier);
            return (numberOfDice, sides, modifier, normalized);
        }

        private static string BuildNormalized(int numberOfDice, int sides, int modifier)
        {
            if (modifier == 0)
                return $"{numberOfDice}d{sides}";

            var sign = modifier > 0 ? "+" : "-";
            var value = Math.Abs(modifier);
            return $"{numberOfDice}d{sides}{sign}{value}";
        }
    }
}
