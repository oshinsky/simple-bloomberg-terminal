
namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Deterministic stand-in for <see cref="IStockApiClient"/> so filing tests run offline.
/// </summary>
public class FakeStockApiClient : IStockApiClient
{
    public const string AppleCik10 = "0000320193"; // matches the seeded Apple company

    public Task<EdgarSubmissions?> GetSubmissions(string cik10)
    {
        if (cik10 != AppleCik10) return Task.FromResult<EdgarSubmissions?>(null);

        // "4" (Form 4) is intentionally ignored by the mapper -> only 2 events created.
        var recent = new EdgarRecent(
            Form: ["10-K", "8-K", "4"],
            FilingDate: ["2023-11-03", "2023-10-01", "2023-09-15"],
            ReportDate: ["2023-09-30", "", ""],
            AccessionNumber: ["0000320193-23-000106", "0000320193-23-000099", "0000320193-23-000088"],
            PrimaryDocument: ["aapl-20230930.htm", "ex99.htm", "form4.xml"],
            PrimaryDocDescription: ["10-K", "8-K", "FORM 4"]);
        return Task.FromResult<EdgarSubmissions?>(new EdgarSubmissions(new EdgarFilings(recent)));
    }

    public Task<string?> ResolveCik(string ticker) =>
        Task.FromResult(string.Equals(ticker, "AAPL", StringComparison.OrdinalIgnoreCase) ? AppleCik10 : null);

    // Reverse of ResolveCik: CIK -> ticker. Only Apple is known.
    public Task<IReadOnlyDictionary<string, string>> GetCikTickerMap() =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string> { [AppleCik10] = "AAPL" });

    public Task<IReadOnlyList<EdgarTicker>> GetTickerEntries() =>
        Task.FromResult<IReadOnlyList<EdgarTicker>>(
            new[] { new EdgarTicker(320193, "AAPL", "Apple Inc.") });

    public Task<string?> GetFilingDocument(string cik, string accessionNoDashes, string primaryDocument) =>
        Task.FromResult<string?>(
            $"FILING {cik}/{accessionNoDashes}/{primaryDocument}\nApple Inc. Form 10-K — total net sales 383,285 (in millions).");
}
