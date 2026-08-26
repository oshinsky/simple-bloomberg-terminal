using System.Text.Json;

namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

/// <summary>Parses the fast-worker and lead-agent JSON contracts used by measurement.</summary>
public static class CounterpartyLedgerCodec
{
    public static IReadOnlyList<CounterpartyClaim> ParseFastWorker(string? response, string section) =>
        ParseArray(response, "sources", el =>
        {
            var name = Text(el, "name");
            return string.IsNullOrWhiteSpace(name)
                ? null
                : new CounterpartyClaim(name!, Text(el, "classification"), Text(el, "note"),
                    Text(el, "evidence"), section);
        });

    public static IReadOnlyList<CounterpartyClaim> ParseLeadAgent(string? response) =>
        ParseArray(response, "items", el =>
        {
            var name = Text(el, "counterparty");
            return string.IsNullOrWhiteSpace(name)
                ? null
                : new CounterpartyClaim(name!, Text(el, "direction"), Text(el, "what"),
                    Text(el, "evidence"), Text(el, "section"));
        });

    private static IReadOnlyList<CounterpartyClaim> ParseArray(
        string? response,
        string property,
        Func<JsonElement, CounterpartyClaim?> map)
    {
        if (!TryParseObject(response, out var document) || document is null) return [];
        using (document)
        {
            if (!document.RootElement.TryGetProperty(property, out var array) ||
                array.ValueKind != JsonValueKind.Array) return [];

            return array.EnumerateArray().Select(map).Where(x => x is not null).Cast<CounterpartyClaim>().ToList();
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
        if (last >= first) text = text[first..(last + 1)];
        else text = text[first..] + "]}";

        try
        {
            document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            // Salvage complete objects from a response cut off in the middle of its final array item.
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
