using System.Text;

namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

public static class CounterpartyIdentity
{
    private static readonly HashSet<string> CorporateSuffixes = new(StringComparer.Ordinal)
    {
        "inc", "corp", "corporation", "co", "ltd", "limited", "llc", "plc", "gmbh", "ag",
        "sa", "nv", "ab", "as", "oy", "kk"
    };

    public static string Key(CounterpartyClaim item) =>
        $"{(item.Direction ?? "").ToUpperInvariant()}|{Normalize(item.Counterparty)}";

    public static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');

        var words = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        // Punctuation normalization turns L.L.C./S.A./N.V. into separate one-letter tokens.
        // Fold those common dotted suffixes before applying the ordinary suffix set.
        if (words.Count >= 3 && words[^3] == "l" && words[^2] == "l" && words[^1] == "c")
            words.RemoveRange(words.Count - 3, 3);
        else if (words.Count >= 2 &&
                 ((words[^2] == "s" && words[^1] == "a") ||
                  (words[^2] == "n" && words[^1] == "v")))
            words.RemoveRange(words.Count - 2, 2);

        while (words.Count > 0 && CorporateSuffixes.Contains(words[^1]))
            words.RemoveAt(words.Count - 1);

        return string.Join(' ', words);
    }
}
