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

/// <summary>What one complete fast-worker-and-lead-agent run produced.</summary>
public sealed record ExtractionRunMetrics(
    int Run,
    int Chunks,
    int FastWorkerClaims,
    int LeadAgentClaims,
    int Errors,
    double FastWorkerEvidencePct = 0,
    double LeadAgentEvidencePct = 0,
    IReadOnlyList<string>? ErrorDetails = null);

/// <summary>One exportable claim with run- and filing-level measurements denormalized onto it.</summary>
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
    string? Section,
    bool EvidenceFound,
    int RunsPresent,
    int WhatVariants,
    int SectionCandidates,
    int RunChunks,
    int RunFastWorkerClaims,
    int RunLeadAgentClaims,
    int RunErrors,
    double FastWorkerEvidencePct,
    double FastWorkerRepeatPct,
    double LeadAgentEvidencePct,
    double LeadAgentRepeatPct,
    double RetentionPct,
    int TotalErrors,
    string Model,
    DateTime RunAt);

/// <summary>The paper-facing result for one filing across repeated complete pipeline runs.</summary>
public sealed record CounterpartyMeasurementResult(
    string Company,
    string Cik,
    string Accession,
    int Runs,
    double MeanChunks,
    double MeanFastWorkerClaims,
    double MeanLeadAgentClaims,
    double FastWorkerEvidencePct,
    double FastWorkerRepeatPct,
    double LeadAgentEvidencePct,
    double LeadAgentRepeatPct,
    double RetentionPct,
    int TotalErrors,
    string Model,
    DateTime RunAt,
    IReadOnlyList<ExtractionRunMetrics> RunDetail,
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

