using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction;

public interface IFilingAnalysisContextService
{
    Task<string> BuildAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        bool scanIfMissing = true,
        string? fastWorkerDigest = null, CancellationToken ct = default);

}
