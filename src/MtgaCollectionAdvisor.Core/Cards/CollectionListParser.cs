using System.Text.RegularExpressions;

namespace MtgaCollectionAdvisor.Core.Cards;

/// <summary>
/// Parses a hand-pasted collection list into name -&gt; quantity pairs. Accepts either
/// the Arena-style "&lt;qty&gt; &lt;name&gt;" per line (same shape as a decklist export,
/// optionally with a trailing "(SET) number"), or simple "Name,Qty" / "Name;Qty" /
/// "Name&lt;tab&gt;Qty" CSV-ish rows - whichever a given line matches.
/// </summary>
public static partial class CollectionListParser
{
    [GeneratedRegex(@"^(?<qty>\d+)\s*[xX]?\s+(?<name>.+?)(?:\s+\([A-Za-z0-9]{2,6}\)\s*[\w\-★]*)?$")]
    private static partial Regex QuantityFirstRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*[,;\t]\s*(?<qty>\d+)$")]
    private static partial Regex NameFirstRegex();

    public static IReadOnlyDictionary<string, int> Parse(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            var match = QuantityFirstRegex().Match(line);
            if (!match.Success) match = NameFirstRegex().Match(line);
            if (!match.Success) continue;

            var name = match.Groups["name"].Value.Trim();
            var qty = int.Parse(match.Groups["qty"].Value);
            if (name.Length == 0 || qty <= 0) continue;

            result[name] = result.TryGetValue(name, out var existing) ? existing + qty : qty;
        }

        return result;
    }
}
