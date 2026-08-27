using System.Text;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Extraction;

// Builds filing-text evidence shared by interactive chat and measurement consumers.
public sealed class FilingAnalysisContextService : IFilingAnalysisContextService
{
    private const int ContextBudgetChars = 60_000;

    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly IFastWorkerScanService _fastWorkerScan;

    public FilingAnalysisContextService(
        ICompanyRepository companies, IStockApiClient client,
        IFastWorkerScanService fastWorkerScan)
    {
        _companies = companies;
        _client = client;
        _fastWorkerScan = fastWorkerScan;
    }

    public async Task<string> BuildAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        bool scanIfMissing = true,
        string? fastWorkerDigest = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc)) return "";

        var digest = fastWorkerDigest is not null
            ? fastWorkerDigest
            : scanIfMissing
                ? await _fastWorkerScan.GetOrCreateFastWorkerDigestAsync(
                    companyId, accession, doc, node, ct)
                : _fastWorkerScan.GetCachedDigest(accession, doc, node) ?? "";
        var filingContext = !string.IsNullOrEmpty(digest)
            ? digest
            : await RawFallbackAsync(companyId, accession, doc, node);
        return string.IsNullOrEmpty(filingContext) ? "" : "\n\n" + filingContext;
    }

    private async Task<string> RawFallbackAsync(
        long companyId, string accession, string doc, ExtractionNode node)
    {
        var company = _companies.GetById(companyId);
        if (company is null || string.IsNullOrWhiteSpace(company.Cik)) return "";
        var raw = await _client.GetFilingDocument(
            Cik.Trim(company.Cik), accession.Replace("-", ""), doc);
        if (raw is null) return "";
        var items = FilingSections.ItemsFor(node);
        var context = BuildContext(raw, items);
        return string.IsNullOrEmpty(context)
            ? ""
            : $"FILING EXCERPTS (Items {string.Join(", ", items)}):\n{context}";
    }

    private static string BuildContext(string raw, string[] items)
    {
        var chunks = FilingSections.Build(raw, items);
        if (chunks.Count == 0) return "";

        var sections = chunks.Select(chunk => chunk.Section).Distinct().ToList();
        var perSection = ContextBudgetChars / sections.Count;
        var used = sections.ToDictionary(section => section, _ => 0);
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            if (used[chunk.Section] + chunk.Text.Length > perSection) continue;
            used[chunk.Section] += chunk.Text.Length;
            sb.Append('[').Append(chunk.Section).Append("]\n")
                .Append(chunk.Text).Append("\n\n");
        }
        return sb.ToString();
    }
}
