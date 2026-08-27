using System.Text;
using System.Text.Json;

namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

/// <summary>Normalizes counterparty identities for repeatability comparisons.</summary>
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

/// <summary>Checks evidence against the exact chunks observed by fast worker agents.</summary>
public sealed class EvidenceIndex
{
    private readonly string _corpus;

    public EvidenceIndex(IEnumerable<ExtractionChunkArtifact> chunks) =>
        _corpus = Normalize(string.Join("\n", chunks.Select(c => c.Text)));

    public bool Contains(string? evidence)
    {
        var quote = Normalize(evidence ?? "");
        return quote.Length > 0 && _corpus.Contains(quote, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Parses the measurement lead agent's ledger response.</summary>
public static class LeadAgentLedgerCodec
{
    public static IReadOnlyList<CounterpartyClaim> Parse(string? response)
    {
        if (!TryParseObject(response, out var document) || document is null) return [];
        using (document)
        {
            if (!document.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array) return [];

            return items.EnumerateArray()
                .Select(element =>
                {
                    var name = Text(element, "counterparty");
                    return string.IsNullOrWhiteSpace(name)
                        ? null
                        : new CounterpartyClaim(name!, Text(element, "direction"), Text(element, "what"),
                            Text(element, "evidence"), Text(element, "section"));
                })
                .Where(claim => claim is not null)
                .Cast<CounterpartyClaim>()
                .ToList();
        }
    }

    private static bool TryParseObject(string? response, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(response)) return false;

        var text = response.Trim();
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        if (first < 0) return false;
        text = last >= first ? text[first..(last + 1)] : text[first..] + "]}";

        try
        {
            document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            var complete = text.LastIndexOf('}');
            if (complete <= 0) return false;
            try
            {
                document = JsonDocument.Parse(text[..(complete + 1)] + "]}");
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    private static string? Text(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }
}
