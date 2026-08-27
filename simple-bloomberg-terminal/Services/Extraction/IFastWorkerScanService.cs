using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction;

// Plans filing chunks, runs fast worker agents, and creates their digest.
public interface IFastWorkerScanService
{
    Task<IReadOnlyList<ExtractionSuggestion>> ScanFullSectionsAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default);

    // Runs the workers and creates a fresh digest for this extraction.
    Task<string> CreateFastWorkerDigestAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default);

    // Builds deterministic filing chunks and runs the fast worker agents.
    Task<FastWorkerScanResult> RunFastWorkerScanAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        Action<FastWorkerScanProgress>? onProgress = null, bool strictCounterparties = false,
        bool captureArtifacts = false,
        CancellationToken ct = default);
}
