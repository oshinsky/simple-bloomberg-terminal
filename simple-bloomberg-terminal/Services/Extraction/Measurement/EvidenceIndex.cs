using System.Text;

namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

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
