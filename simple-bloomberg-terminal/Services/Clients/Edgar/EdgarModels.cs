using System.Text.Json.Serialization;

namespace simple_bloomberg_terminal.Services.Clients.Edgar;

// Minimal shapes for the SEC EDGAR JSON we actually read. System.Net.Http.Json uses
// JsonSerializerDefaults.Web (camelCase, case-insensitive), so only the keys that don't
// match a C# property name by case (hyphens, snake_case) need an explicit attribute.

// /submissions/CIK{cik10}.json (parallel arrays under filings.recent)
public record EdgarSubmissions(EdgarFilings? Filings);

public record EdgarFilings(EdgarRecent? Recent);

// Parallel arrays: index i describes one filing. SEC keys are camelCase => no attrs needed.
// Extra fields beyond Form/FilingDate are optional so existing call sites stay valid.
public record EdgarRecent(
    List<string>? Form,
    List<string>? FilingDate,
    List<string>? ReportDate = null,
    List<string>? AccessionNumber = null,
    List<string>? PrimaryDocument = null,
    List<string>? PrimaryDocDescription = null);

// https://www.sec.gov/files/company_tickers.json (numeric-string-keyed map)
public record EdgarTicker(
    [property: JsonPropertyName("cik_str")] long CikStr,
    string Ticker,
    string Title);
