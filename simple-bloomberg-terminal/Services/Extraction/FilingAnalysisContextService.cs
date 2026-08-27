using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Extraction;

// Builds fast-worker findings shared by interactive chat and measurement consumers.
public sealed class FilingAnalysisContextService : IFilingAnalysisContextService
{
    private readonly IFastWorkerScanService _fastWorkerScan;

    public FilingAnalysisContextService(IFastWorkerScanService fastWorkerScan)
    {
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
                ? await _fastWorkerScan.CreateFastWorkerDigestAsync(
                    companyId, accession, doc, node, ct)
                : "";
        return string.IsNullOrEmpty(digest) ? "" : "\n\n" + digest;
    }
}
