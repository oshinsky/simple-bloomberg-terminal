using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction;

// Plans filing chunks, runs fast worker agents, and creates their digest.
public interface IFastWorkerScanService
{
    string? GetCachedDigest(string accession, string doc, ExtractionNode node);

    Task<IReadOnlyList<ExtractionSuggestion>> ScanFullSectionsAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default);

    // Returns a cached fast-worker digest, running the scan when it is missing.
    Task<string> GetOrCreateFastWorkerDigestAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default);

    // Builds deterministic filing chunks and runs the fast worker agents.
    Task<FastWorkerScanResult> RunFastWorkerScanAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        Action<FastWorkerScanProgress>? onProgress = null, bool strictCounterparties = false,
        bool captureArtifacts = false,
        CancellationToken ct = default);
}
