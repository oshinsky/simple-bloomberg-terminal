using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

// Coordinates repeated COST counterparty runs and delegates to the pure measurement calculator.
// Each run gets an independent scan and lead-agent call while deterministic artifacts remain cached.
public class CounterpartyMeasurementService
{
    private const int MaxParallelRuns = 10;
    private const int LeadAgentMaxTokens = 16_000;

    private readonly ICompanyRepository _companies;
    private readonly IChatLlm _llm;
    private readonly IServiceScopeFactory _scopes;
    private readonly IUserApiKeyProvider _keys;
    private readonly ILogger<CounterpartyMeasurementService> _logger;

    public CounterpartyMeasurementService(
        ICompanyRepository companies,
        IChatLlm llm,
        IServiceScopeFactory scopes,
        IUserApiKeyProvider keys,
        ILogger<CounterpartyMeasurementService> logger)
    {
        _companies = companies;
        _llm = llm;
        _scopes = scopes;
        _keys = keys;
        _logger = logger;
    }

    public async Task<CounterpartyMeasurementResult> MeasureAsync(
        long companyId,
        string accession,
        string doc,
        int runs,
        string? form = null,
        bool strictCounterparties = false,
        Action<MeasureProgress>? onProgress = null,
        CancellationToken ct = default)
    {
        const ExtractionNode node = ExtractionNode.COST;
        var company = _companies.GetById(companyId);
        var target = new FilingTarget(
            companyId,
            company?.Name ?? $"#{companyId}",
            company?.Cik ?? "",
            accession,
            doc,
            form);
        var model = await ModelLabelAsync(ct);
        var runAt = DateTime.UtcNow;
        var keys = await _keys.GetAsync(ct);
        // Warm deterministic SEC and parsing caches before concurrent repetitions begin.
        var first = await ExecuteRunAsync(
            target, node, strictCounterparties, 1, null,
            keys, model, onProgress, ct);

        using var gate = new SemaphoreSlim(MaxParallelRuns);
        var rest = await Task.WhenAll(
            Enumerable.Range(2, Math.Max(0, runs - 1)).Select(run => ExecuteRunAsync(
                target, node, strictCounterparties, run, gate,
                keys, model, onProgress, ct)));

        return MeasurementCalculator.Calculate(
            rest.Prepend(first).ToArray(), model, runAt);
    }

    // Runs one complete fast-worker map and lead-agent reduce cycle.
    private async Task<CounterpartyRunResult> ExecuteRunAsync(
        FilingTarget target,
        ExtractionNode node,
        bool strictCounterparties,
        int run,
        SemaphoreSlim? gate,
        UserApiKeys keys,
        string model,
        Action<MeasureProgress>? onProgress,
        CancellationToken ct)
    {
        if (gate is not null) await gate.WaitAsync(ct);
        try
        {
            // Concurrent runs need independent scopes because their services use scoped DbContexts.
            using var scope = _scopes.CreateScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var fastWorkerScan = services.GetRequiredService<IFastWorkerScanService>();
            var context = services.GetRequiredService<IFilingAnalysisContextService>();
            var leadAgent = services.GetRequiredService<ILeadAgentRunner>();

            var chunkItems = new Dictionary<int, string>();
            var errors = new List<string>();

            var scanned = await fastWorkerScan.RunFastWorkerScanAsync(
                target.CompanyId,
                target.Accession,
                target.Document,
                node,
                progress =>
                {
                    // Fast-worker callbacks arrive concurrently from the six-wide scan pool.
                    lock (errors)
                    {
                        switch (progress.Phase)
                        {
                            case FastWorkerChunkPhase.Planned when progress.Plan is { } plan:
                                foreach (var chunk in plan) chunkItems[chunk.Index] = chunk.Item;
                                onProgress?.Invoke(new MeasureProgress(run, "plan", Plan: plan));
                                break;

                            case FastWorkerChunkPhase.Running:
                                onProgress?.Invoke(new MeasureProgress(
                                    run, "chunk-running", ChunkIndex: progress.Index));
                                break;

                            case FastWorkerChunkPhase.Error:
                                var title = chunkItems.TryGetValue(progress.Index, out var failedItem)
                                    ? failedItem
                                    : $"chunk {progress.Index + 1}";
                                errors.Add($"{title}: {progress.Response ?? "Unknown worker error."}");
                                onProgress?.Invoke(new MeasureProgress(
                                    run, "chunk-error", ChunkIndex: progress.Index, Error: progress.Response));
                                break;

                            case FastWorkerChunkPhase.Done:
                                onProgress?.Invoke(new MeasureProgress(
                                    run, "chunk-done", ChunkIndex: progress.Index, Found: progress.Found));
                                break;
                        }
                    }
                },
                strictCounterparties,
                captureArtifacts: true,
                ct);

            var fastWorkerClaims = (scanned.WorkerClaims ?? [])
                .Select(finding => new CounterpartyClaim(
                    finding.RelatedCompany ?? finding.Name,
                    node == ExtractionNode.COST ? "SUPPLIER" : "CUSTOMER",
                    finding.Note,
                    finding.Evidence,
                    finding.Section))
                .ToList();
            onProgress?.Invoke(new MeasureProgress(
                run, "fast-worker-scan-done", FastWorkerClaims: fastWorkerClaims.Count));

            var (leadAgentClaims, leadAgentError) = await RunLeadAgentAsync(
                context, leadAgent, target, node, scanned.FastWorkerDigest, run, model, ct);
            if (leadAgentError is not null)
            {
                errors.Add($"Lead agent: {leadAgentError}");
                onProgress?.Invoke(new MeasureProgress(run, "lead-agent-error", Error: leadAgentError));
            }
            onProgress?.Invoke(new MeasureProgress(
                run, "lead-agent-done", LeadAgentClaims: leadAgentClaims.Count));

            return new CounterpartyRunResult(
                run, target, scanned.Corpus ?? [], fastWorkerClaims, leadAgentClaims, errors);
        }
        finally
        {
            gate?.Release();
        }
    }

    private async Task<(IReadOnlyList<CounterpartyClaim> Claims, string? Error)> RunLeadAgentAsync(
        IFilingAnalysisContextService context,
        ILeadAgentRunner leadAgent,
        FilingTarget target,
        ExtractionNode node,
        string fastWorkerDigest,
        int run,
        string model,
        CancellationToken ct)
    {
        try
        {
            var filingContext = await context.BuildAsync(
                target.CompanyId,
                target.Accession,
                target.Document,
                node,
                scanIfMissing: false,
                fastWorkerDigest: fastWorkerDigest,
                ct: ct);

            _logger.LogInformation(
                "Measurement lead agent starting for {Company} run {Run} using {Model}; digestChars={DigestChars}, contextChars={ContextChars}, maxTokens={MaxTokens}",
                target.Company, run, model, fastWorkerDigest.Length, filingContext.Length, LeadAgentMaxTokens);

            var completion = await leadAgent.CompleteAsync(
                MeasurementPrompts.LeadAgentSystemPrompt,
                filingContext,
                MeasurementPrompts.LeadAgentUserPrompt,
                LeadAgentMaxTokens,
                ct);

            if (string.Equals(completion.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                _logger.LogWarning(
                    "Measurement lead agent reached its output limit for {Company} run {Run}; responseChars={ResponseChars}",
                    target.Company, run, completion.Content.Length);

            return (LeadAgentLedgerCodec.Parse(completion.Content), null);
        }
        catch (Exception ex) when (
            !ct.IsCancellationRequested &&
            ex is HttpRequestException or IOException or TaskCanceledException)
        {
            _logger.LogError(
                ex,
                "Measurement lead agent failed for {Company} run {Run} using {Model}; digestChars={DigestChars}",
                target.Company, run, model, fastWorkerDigest.Length);
            return ([], ex.Message);
        }
    }

    private async Task<string> ModelLabelAsync(CancellationToken ct)
    {
        try
        {
            var (provider, model) = await _llm.ResolveParsingAsync(ct);
            return $"{provider}/{model} | worker={CounterpartyPrompts.Version} | lead={MeasurementPrompts.Version}";
        }
        catch (Exception ex) when (
            !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        {
            return "unknown";
        }
    }
}
