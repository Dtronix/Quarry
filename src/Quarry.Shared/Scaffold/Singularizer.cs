using System;
using System.Collections.Generic;

namespace Quarry.Shared.Scaffold;

internal static class Singularizer
{
    private static readonly Dictionary<string, string> Irregulars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["people"] = "person",
        ["men"] = "man",
        ["women"] = "woman",
        ["children"] = "child",
        ["mice"] = "mouse",
        ["geese"] = "goose",
        ["teeth"] = "tooth",
        ["feet"] = "foot",
        ["oxen"] = "ox",
        ["knives"] = "knife",
        ["wives"] = "wife",
        ["lives"] = "life",
        // "data" moved to Uncountable — "datum" is surprising for DB table names
        ["indices"] = "index",
        ["matrices"] = "matrix",
        ["vertices"] = "vertex",
        ["criteria"] = "criterion",
        ["phenomena"] = "phenomenon"
    };

    private static readonly HashSet<string> Uncountable = new(StringComparer.OrdinalIgnoreCase)
    {
        "equipment", "information", "rice", "money", "species",
        "series", "fish", "sheep", "deer", "aircraft", "status", "data"
    };

    public static string Singularize(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        if (Uncountable.Contains(word))
            return word;

        if (Irregulars.TryGetValue(word, out var irregular))
            return MatchCase(word, irregular);

        // Rule-based singularization (order matters)
        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 4)
        {
            // categories -> category, but not "series"
            return word.Substring(0, word.Length - 3) + MatchLastCase(word, "y");
        }

        if (word.EndsWith("ves", StringComparison.OrdinalIgnoreCase) && word.Length > 4)
        {
            if (word.EndsWith("ives", StringComparison.OrdinalIgnoreCase))
            {
                // olives -> olive, archives -> archive, objectives -> objective
                // Special cases (knives/wives/lives -> knife/wife/life) handled by Irregulars
                return word.Substring(0, word.Length - 1);
            }
            // wolves -> wolf, halves -> half
            var stemF = word.Substring(0, word.Length - 3);
            return stemF + MatchLastCase(word, "f");
        }

        if (word.EndsWith("sses", StringComparison.OrdinalIgnoreCase))
        {
            // addresses -> address, classes -> class
            return word.Substring(0, word.Length - 2);
        }

        if (word.EndsWith("ses", StringComparison.OrdinalIgnoreCase) && word.Length > 4)
        {
            var preceding = word[word.Length - 4];
            // buses -> bus, houses -> house
            if (preceding == 'u' || preceding == 'U')
                return word.Substring(0, word.Length - 2);
            // responses -> response, databases -> database
            return word.Substring(0, word.Length - 1);
        }

        if (word.EndsWith("zes", StringComparison.OrdinalIgnoreCase) && word.Length > 4)
        {
            // quizzes -> quiz (double z)
            if (word.Length > 4 && word[word.Length - 4] == word[word.Length - 3])
                return word.Substring(0, word.Length - 3);
            return word.Substring(0, word.Length - 1);
        }

        if (word.EndsWith("ches", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("shes", StringComparison.OrdinalIgnoreCase))
        {
            // matches -> match, dishes -> dish
            return word.Substring(0, word.Length - 2);
        }

        if (word.EndsWith("xes", StringComparison.OrdinalIgnoreCase))
        {
            // boxes -> box, indexes -> index
            return word.Substring(0, word.Length - 2);
        }

        if (word.EndsWith("oes", StringComparison.OrdinalIgnoreCase))
        {
            // heroes -> hero, potatoes -> potato
            return word.Substring(0, word.Length - 2);
        }

        if (word.Length > 1 &&
            word.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("us", StringComparison.OrdinalIgnoreCase))
        {
            // users -> user, products -> product
            return word.Substring(0, word.Length - 1);
        }

        return word;
    }

    private static string MatchCase(string source, string target)
    {
        if (source.Length == 0) return target;
        if (char.IsUpper(source[0]))
            return char.ToUpperInvariant(target[0]) + target.Substring(1);
        return target;
    }

    private static string MatchLastCase(string source, string replacement)
    {
        if (source.Length == 0) return replacement;
        if (char.IsUpper(source[source.Length - 1]))
            return replacement.ToUpperInvariant();
        return replacement;
    }
}
