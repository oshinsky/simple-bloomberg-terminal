namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

/// <summary>The immutable identity of one filing used in an experiment.</summary>
public sealed record FilingTarget(
    long CompanyId,
    string Company,
    string Cik,
    string Accession,
    string Document,
    string? Form = null);

/// <summary>One exact excerpt supplied to a fast worker agent.</summary>
public sealed record ExtractionChunkArtifact(
    int Index,
    string Item,
    IReadOnlyList<string> Titles,
    string Text);

/// <summary>A standardized claim emitted by either a fast worker agent or the lead agent.</summary>
public sealed record CounterpartyClaim(
    string Counterparty,
    string? Direction,
    string? What,
    string? Evidence,
    string? Section);

/// <summary>One raw claim used by the comparison and manual-review views.</summary>
public sealed record CounterpartyMeasurementRow(
    string Layer,
    string Company,
    string Cik,
    string Accession,
    string Doc,
    int Run,
    string Counterparty,
    string? Direction,
    string? What,
    string? Evidence,
    string? Section);

/// <summary>The paper-facing result for one filing across repeated complete pipeline runs.</summary>
public sealed record CounterpartyMeasurementResult(
    string Company,
    string Cik,
    string Accession,
    int Runs,
    int TotalErrors,
    string Model,
    DateTime RunAt,
    IReadOnlyList<CounterpartyMeasurementRow> Rows,
    string? Error = null);

/// <summary>All observable output from one complete measurement run.</summary>
public sealed record CounterpartyRunResult(
    int Run,
    FilingTarget Target,
    IReadOnlyList<ExtractionChunkArtifact> Corpus,
    IReadOnlyList<CounterpartyClaim> FastWorkerClaims,
    IReadOnlyList<CounterpartyClaim> LeadAgentClaims,
    IReadOnlyList<string> Errors);
