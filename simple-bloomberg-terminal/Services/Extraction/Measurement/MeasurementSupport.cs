using System.Text.Json;

namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

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
