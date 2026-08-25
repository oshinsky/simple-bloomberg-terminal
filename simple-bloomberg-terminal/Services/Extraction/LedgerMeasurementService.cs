using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Extraction;

/// <summary>One extracted claim, from either layer. WORKER rows are one fast agent's raw output for
/// one chunk (pre-merge); LEAD rows are the strong agent's ledger.</summary>
public record LedgerItem(
    string Counterparty, string? Direction, string? What, string? Evidence, string? Section);

/// <summary>What one run cost and produced at each layer — the fast model's chunk/finding counts
/// alongside the strong model's item count, so the two can be graded separately.</summary>
/// <param name="Errors">Chunks whose worker call failed or returned unparseable JSON. A failed worker
/// is swallowed by ScanChunkAsync and yields an empty list — correct for production, poison for
/// measurement, because it is indistinguishable from an honest "nothing here" and quietly deflates
/// the run's yield. Counting it is what lets a clean run be told apart from a degraded one.</param>
public record RunStats(
    int Run, int Chunks, int WorkerItems, int LeadItems, int Errors,
    double WorkerEvidencePct = 0, double LeadEvidencePct = 0,
    IReadOnlyList<string>? ErrorDetails = null);

/// <summary>One CSV line: a single claim from one layer of one run, with the run- and filing-level
/// figures denormalised onto it so a single sheet answers every question.</summary>
public record LedgerRow(
    string Layer, string Company, string Cik, string Accession, string Doc, int Run,
    string Counterparty, string? Direction, string? What, string? Evidence, string? Section,
    bool EvidenceFound,
    int RunsPresent, int WhatVariants, int SectionCandidates,
    int RunChunks, int RunWorkerItems, int RunLeadItems, int RunErrors,
    double WorkerEvidencePct, double WorkerRepeatPct, double LeadEvidencePct, double LeadRepeatPct,
    double RetentionPct, int TotalErrors, string Model, DateTime RunAt);

/// <summary>The paper-facing row: one filing, both layers. Precision is absent by design — it is the
/// manual pass, computed from the annotated `judgement` column.</summary>
public record FilingMeasurement(
    string Company, string Cik, string Accession, int Runs,
    double MeanChunks, double MeanWorkerItems, double MeanLeadItems,
    double WorkerEvidencePct, double WorkerRepeatPct,
    double LeadEvidencePct, double LeadRepeatPct, double RetentionPct, int TotalErrors,
    string Model, DateTime RunAt,
    IReadOnlyList<RunStats> RunDetail, IReadOnlyList<LedgerRow> Rows, string? Error = null);

/// <summary>
/// Measurement harness for the COST extraction pipeline. Runs the SAME extraction of the SAME filing
/// N times and scores both layers separately:
///
/// <list type="bullet">
/// <item><b>Repeatability</b> — in how many of the N runs each counterparty appeared. Variation in
/// the free-text `what` is counted separately
/// (<c>WhatVariants</c>) and never as instability. Scored per layer.</item>
/// <item><b>Evidence presence</b> — whether each claim's complete `evidence` occurs in the filing text
/// after normalising case, punctuation and whitespace. Scored per layer.</item>
/// <item><b>Precision</b> — not computed here. The CSV ships a blank `judgement` column for the
/// manual annotation of one run.</item>
/// </list>
///
/// EVERY RUN IS A FULL PIPELINE RUN: its own fast-worker scan (triage + per-chunk workers) followed by
/// its own lead-agent call, and runs execute CONCURRENTLY. The deterministic caches (raw filing,
/// parsed headings, rendered reports, tagged XBRL) are shared on purpose — reusing a fetch or a parse
/// removes no model variance and avoids pulling a multi-MB document from SEC N times.
///
/// What makes concurrency safe is that each run's lead call is handed its OWN scan's digest rather
/// than resolving one: the <c>filing-findings</c> cache key is per-filing, so N concurrent runs all
/// write it and a resolving run would be graded against whichever scan finished last.
/// </summary>
public class LedgerMeasurementService
{
    private readonly ICompanyRepository _companies;
    private readonly IChatLlm _llm;
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopes;
    private readonly IUserApiKeyProvider _keys;

    // Concurrent full-pipeline runs. Each run fans its workers out 6-wide (ScanChunksAsync), so this
    // is the multiplier on worker concurrency: 10 runs => up to 60 simultaneous fast calls. DeepSeek
    // limits CONCURRENT CONNECTIONS rather than requests/minute — 2,500 on the fast tier and 500 on
    // the strong one — so 60 fast + 10 strong sits at a couple of percent of budget. Note the ceiling
    // is a property of the routed provider, not of this code: IChatLlm resolves the user's chosen
    // ParsingProvider and its automatic model tier; a tier-limited provider would need this lower.
    private const int MaxParallelRuns = 10;

    private const string Worker = "WORKER";
    private const string Lead = "LEAD";

    // The scan and chat services are resolved PER RUN from their own DI scope rather than injected,
    // because concurrent runs must not share a DbContext. See RunPipelineAsync.
    public LedgerMeasurementService(
        ICompanyRepository companies, IChatLlm llm, IMemoryCache cache,
        IServiceScopeFactory scopes, IUserApiKeyProvider keys)
    {
        _companies = companies;
        _llm = llm;
        _cache = cache;
        _scopes = scopes;
        _keys = keys;
    }

    public async Task<FilingMeasurement> MeasureAsync(
        long companyId, string accession, string doc, int runs, string? filingType = null,
        bool strictCounterparties = false,
        Action<MeasureProgress>? onProgress = null, CancellationToken ct = default)
    {
        const ExtractionNode node = ExtractionNode.COST;
        var company = _companies.GetById(companyId);
        var name = company?.Name ?? $"#{companyId}";
        var cik = company?.Cik ?? "";
        var model = await ModelLabelAsync(ct);
        var at = DateTime.UtcNow;

        var sectionCandidates = new Dictionary<(int Run, string Section), int>();

        // Each run needs its own DI scope, so its keys travel with it into the per-run services.
        var keys = await _keys.GetAsync(ct);

        // Run 1 goes FIRST, alone. It is a warm-up: the filing document, its parsed headings and the
        // rendered Item 8 reports are all cache misses on a cold filing, and fanning out N runs into
        // those misses would have every run fetch them simultaneously — one 10-K plus up to twenty
        // R*.htm files, times N, arriving at SEC at once. SEC throttles hard at that rate, and a
        // throttled fetch degrades the scan rather than failing it, which is exactly the silent bias
        // the error counter exists to catch. One run first fills those caches for everyone.
        //
        // It costs one run's latency, not much: 10 runs still finish in roughly the time of two.
        var first = await RunPipelineAsync(
            companyId, accession, doc, node, filingType, strictCounterparties, 1, null,
            sectionCandidates, keys, onProgress, ct);

        // The rest go WIDE. They no longer contend on the findings cache either, because each lead
        // call is handed its own scan's digest instead of resolving it from that shared key.
        using var gate = new SemaphoreSlim(MaxParallelRuns);
        var rest = await Task.WhenAll(
            Enumerable.Range(2, Math.Max(0, runs - 1)).Select(run => RunPipelineAsync(
                companyId, accession, doc, node, filingType, strictCounterparties, run, gate, sectionCandidates,
                keys, onProgress, ct)));

        var perRun = rest.Prepend(first).ToArray();
        var stats = perRun.Select(r => r.Stats).OrderBy(s => s.Run).ToList();
        var claims = perRun
            .SelectMany(r => r.Worker.Select(w => (Layer: Worker, r.Stats.Run, Item: w))
                .Concat(r.Lead.Select(l => (Layer: Lead, r.Stats.Run, Item: l))))
            .ToList();

        // The corpus is the filing text, identical for every run; building it after the runs means
        // the raw document and the rendered reports are certain to be in cache.
        var evidenceIndex = new EvidenceIndex(BuildCorpus(accession, doc, node, filingType));
        // Identity for repeatability is (direction, normalised counterparty) — raw string equality
        // would score "Acme Foundry" and "Acme Foundry Ltd." as two different companies and understate
        // stability for what is plainly one relationship.
        var keyed = claims.Select(c => (c.Layer, c.Run, c.Item, Key: KeyOf(c.Item))).ToList();

        // Checked BY POSITION, not in a dictionary keyed on the claim: a run can legitimately list
        // the same counterparty twice with identical fields. Distinct quotes are checked once because
        // the same evidence commonly recurs across runs and across the two layers.
        var cachedGrounding = new Dictionary<string, bool>(StringComparer.Ordinal);
        var evidenceFound = keyed
            .Select(x => x.Item.Evidence is { } e
                ? cachedGrounding.TryGetValue(e, out var found)
                    ? found
                    : cachedGrounding[e] = evidenceIndex.Contains(e)
                : false)
            .ToList();

        // Per layer: a counterparty group is stable when it appears in EVERY run of that layer.
        var groups = keyed
            .Select((x, i) => (x, i))
            .GroupBy(t => (t.x.Layer, t.x.Key))
            .ToDictionary(g => g.Key, g => g.Select(t => t.i).ToList());

        // run: null pools every run (the filing-level figure); a value selects one run.
        double EvidenceOf(string layer, int? run = null)
        {
            var idx = keyed.Select((x, i) => (x, i))
                .Where(t => t.x.Layer == layer && (run is null || t.x.Run == run))
                .Select(t => t.i).ToList();
            return idx.Count == 0 ? 0 : 100.0 * idx.Count(i => evidenceFound[i]) / idx.Count;
        }
        double RepeatOf(string layer)
        {
            var g = groups.Where(kv => kv.Key.Layer == layer).ToList();
            return g.Count == 0
                ? 0
                : 100.0 * g.Count(kv => kv.Value.Select(i => keyed[i].Run).Distinct().Count() == runs) / g.Count;
        }

        // Evidence presence IS meaningful per run (each claim is checked on its own), unlike repeatability,
        // which only exists across runs. Fold it back in so the per-run table can show it.
        stats = stats
            .Select(s => s with
            {
                WorkerEvidencePct = Math.Round(EvidenceOf(Worker, s.Run), 1),
                LeadEvidencePct = Math.Round(EvidenceOf(Lead, s.Run), 1),
            })
            .ToList();

        var totalErrors = stats.Sum(s => s.Errors);
        var meanChunks = stats.Count == 0 ? 0 : stats.Average(s => s.Chunks);
        var meanWorker = stats.Count == 0 ? 0 : stats.Average(s => s.WorkerItems);
        var meanLead = stats.Count == 0 ? 0 : stats.Average(s => s.LeadItems);
        var workerEvidence = Math.Round(EvidenceOf(Worker), 1);
        var workerRepeat = Math.Round(RepeatOf(Worker), 1);
        var leadEvidence = Math.Round(EvidenceOf(Lead), 1);
        var leadRepeat = Math.Round(RepeatOf(Lead), 1);
        // How much of the fast layer's output the strong layer carried through. Recall is the hole in
        // the metric set — evidence presence and precision only see what WAS reported, never what was
        // silently dropped — and this is the cheapest available proxy for it.
        var retention = meanWorker == 0 ? 0 : Math.Round(100.0 * meanLead / meanWorker, 1);

        var byRun = stats.ToDictionary(s => s.Run);
        var rows = keyed.Select((x, i) =>
        {
            var g = groups[(x.Layer, x.Key)];
            var st = byRun[x.Run];
            return new LedgerRow(
                x.Layer, name, cik, accession, doc, x.Run,
                x.Item.Counterparty, x.Item.Direction, x.Item.What, x.Item.Evidence, x.Item.Section,
                evidenceFound[i],
                g.Select(j => keyed[j].Run).Distinct().Count(),
                g.Select(j => Norm(keyed[j].Item.What ?? "")).Distinct().Count(),
                x.Item.Section is { } sec ? sectionCandidates.GetValueOrDefault((x.Run, sec)) : 0,
                st.Chunks, st.WorkerItems, st.LeadItems, st.Errors,
                workerEvidence, workerRepeat, leadEvidence, leadRepeat, retention, totalErrors,
                model, at);
        }).ToList();

        return new FilingMeasurement(
            name, cik, accession, runs,
            Math.Round(meanChunks, 2), Math.Round(meanWorker, 2), Math.Round(meanLead, 2),
            workerEvidence, workerRepeat, leadEvidence, leadRepeat, retention, totalErrors,
            model, at, stats, rows);
    }

    /// <summary>
    /// One complete pipeline run: its own triage + worker fan-out, then its own lead call grounded on
    /// the digest THAT scan produced. Passing the digest explicitly is what makes runs safe to
    /// parallelise — resolving it would go through the one <c>filing-findings</c> cache key that every
    /// concurrent run of this filing writes, so a run could be graded against a different run's scan.
    /// </summary>
    private async Task<(RunStats Stats, List<LedgerItem> Worker, List<LedgerItem> Lead)> RunPipelineAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType,
        bool strictCounterparties, int run, SemaphoreSlim? gate,
        Dictionary<(int, string), int> sectionCandidates,
        UserApiKeys keys, Action<MeasureProgress>? onProgress, CancellationToken ct)
    {
        if (gate is not null) await gate.WaitAsync(ct);
        try
        {
            // A scope of its own — and with it, its own DbContext. ICompanyRepository is scoped and
            // both services below reach for it (company CIK lookups on every fetch), so sharing one
            // scope across concurrent runs meant concurrent operations on one DbContext: "A second
            // operation was started on this context instance before a previous operation completed."
            // Keys must be re-Set here: a fresh scope has no HttpContext to resolve them from.
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var scan = sp.GetRequiredService<IFilingExtractionService>();
            var chat = sp.GetRequiredService<IExtractionChatService>();

            var chunkItem = new Dictionary<int, string>();
            var workerItems = new List<LedgerItem>();
            var chunkFound = new Dictionary<int, int>();
            var errorDetails = new List<string>();
            var errors = 0;

            // onProgress fires from the 6-wide worker pool, so every handler touching shared state
            // takes the lock — including the Planned event, which races the first Done.
            var scanned = await scan.ScanAutoAsync(companyId, accession, doc, node, filingType, p =>
            {
                lock (workerItems)
                {
                    if (p.Phase == ScanChunkPhase.Planned && p.Plan is { } plan)
                    {
                        foreach (var c in plan) chunkItem[c.Index] = c.Item;
                        onProgress?.Invoke(new MeasureProgress(run, "plan", Plan: plan));
                    }
                    else if (p.Phase == ScanChunkPhase.Running)
                        onProgress?.Invoke(new MeasureProgress(run, "chunk-running", ChunkIndex: p.Index));
                    else if (p.Phase == ScanChunkPhase.Error)
                    {
                        errors++;
                        var title = chunkItem.TryGetValue(p.Index, out var failedItem)
                            ? $"Item {failedItem}"
                            : $"chunk {p.Index + 1}";
                        errorDetails.Add($"{title}: {p.Response ?? "Unknown worker error."}");
                        onProgress?.Invoke(new MeasureProgress(
                            run, "chunk-error", ChunkIndex: p.Index, Error: p.Response));
                    }
                    else if (p.Phase == ScanChunkPhase.Done)
                    {
                        chunkFound[p.Index] = p.Found;
                        onProgress?.Invoke(new MeasureProgress(run, "chunk-done", ChunkIndex: p.Index, Found: p.Found));
                        // ScanProgress already carries the worker's raw reply for the UI's "under the
                        // hood" inspector, so the fast layer can be graded without touching
                        // IFilingExtractionService. These are PRE-merge, which is the right unit: the
                        // question is what one worker read out of one chunk, not what survived Combine.
                        var section = chunkItem.TryGetValue(p.Index, out var it) ? $"Item {it}" : "?";
                        workerItems.AddRange(ParseWorkerSources(p.Response, section));
                    }
                }
            }, strictCounterparties, ct);

            onProgress?.Invoke(new MeasureProgress(run, "scan-done", WorkerItems: workerItems.Count));

            var lead = await RunOnceAsync(chat, companyId, accession, doc, node, filingType, scanned.Digest, ct);

            onProgress?.Invoke(new MeasureProgress(run, "lead-done", LeadItems: lead.Count));

            lock (sectionCandidates)
                foreach (var kv in chunkFound)
                {
                    var key = (run, chunkItem.TryGetValue(kv.Key, out var it) ? $"Item {it}" : "?");
                    sectionCandidates[key] = sectionCandidates.GetValueOrDefault(key) + kv.Value;
                }

            return (new RunStats(run, scanned.Scanned, workerItems.Count, lead.Count, errors,
                        ErrorDetails: errorDetails),
                    workerItems, lead);
        }
        finally { gate?.Release(); }
    }

    // One fast worker's raw reply, as the scan's progress feed already exposes it. Field names are the
    // worker envelope's; the COST prompt states them in counterparty terms (name = the counterparty,
    // classification = direction of trade, note = what is traded).
    private static IEnumerable<LedgerItem> ParseWorkerSources(string? response, string section)
    {
        if (string.IsNullOrWhiteSpace(response)) yield break;
        using var parsed = LlmJson.ParseObject(response, "]}");
        if (parsed is null ||
            !parsed.RootElement.TryGetProperty("sources", out var arr) ||
            arr.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in arr.EnumerateArray())
        {
            var nm = LlmJson.Str(el, "name");
            if (string.IsNullOrWhiteSpace(nm)) continue;
            yield return new LedgerItem(
                nm!, LlmJson.Str(el, "classification"), LlmJson.Str(el, "note"),
                LlmJson.Str(el, "evidence"), section);
        }
    }

    // One run: a single-turn conversation carrying nothing but the fixed ledger prompt. A run that
    // fails or returns unparseable output yields an empty list — it still counts in the denominator,
    // which is correct: a run that produced no ledger is a repeatability failure, not a missing sample.
    private static async Task<List<LedgerItem>> RunOnceAsync(
        IExtractionChatService chat,
        long companyId, string accession, string doc, ExtractionNode node, string? filingType,
        string grounding, CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            var history = new[] { new ChatMessage("user", ExtractionChatService.LedgerPrompt) };
            await foreach (var delta in chat.StreamReplyAsync(
                companyId, accession, doc, node, history, filingType, handoff: false,
                grounding: grounding, ct: ct))
                // "text" only — "reasoning" and "status" are not part of the answer.
                if (delta.Kind == "text") sb.Append(delta.Text);

            return ParseLedger(sb.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    // Pull the items out of the ```ledger``` block. LlmJson.ParseObject already tolerates fences and
    // surrounding prose, and the "]}"-salvage recovers a reply cut off mid-array.
    internal static List<LedgerItem> ParseLedger(string answer)
    {
        var items = new List<LedgerItem>();
        using var doc = LlmJson.ParseObject(answer, "]}");
        if (doc is null ||
            !doc.RootElement.TryGetProperty("items", out var arr) ||
            arr.ValueKind != JsonValueKind.Array) return items;

        foreach (var el in arr.EnumerateArray())
        {
            var cp = LlmJson.Str(el, "counterparty");
            if (string.IsNullOrWhiteSpace(cp)) continue;
            items.Add(new LedgerItem(
                cp!, LlmJson.Str(el, "direction"), LlmJson.Str(el, "what"),
                LlmJson.Str(el, "evidence"), LlmJson.Str(el, "section")));
        }
        return items;
    }

    private static string KeyOf(LedgerItem i) => $"{(i.Direction ?? "").ToUpperInvariant()}|{Norm(i.Counterparty)}";

    // Identity normalisation: case, punctuation and the corporate suffixes filings use inconsistently
    // ("Acme Foundry Ltd." vs "Acme Foundry"). Applied to counterparty names and to `what` when
    // counting its variants.
    private static readonly string[] Suffixes =
        ["inc", "inc.", "corp", "corp.", "corporation", "co", "co.", "ltd", "ltd.", "limited",
         "llc", "l.l.c.", "plc", "gmbh", "ag", "sa", "s.a.", "nv", "n.v.", "ab", "as", "oy", "kk"];

    private static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Suffixes.Contains(w))
            .ToList();
        return string.Join(' ', words);
    }

    private async Task<string> ModelLabelAsync(CancellationToken ct)
    {
        try
        {
            var (provider, model) = await _llm.ResolveParsingAsync(ct);
            return $"{provider}/{model}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return "unknown"; }
    }

    // Everything the workers were given, so an honest quote can always be found in it. That is TWO
    // sources, not one:
    //
    //   1. the cleaned Item text from the filing document (triaged headings + thin-Item chunks), and
    //   2. Item 8, which does NOT come from the filing document at all — ScanAutoAsync reads it from
    //      the SEC's own rendered reports (R*.htm), fetched and cached separately.
    //
    // Leaving (2) out marked every financial-statement quote as absent even when it was verbatim
    // and correct, because the text simply was not in the file being searched.
    //
    // Empty when nothing is cached — evidence presence is then 0 for everything, which is visible in
    // the output rather than silently passing.
    private string BuildCorpus(string accession, string doc, ExtractionNode node, string? filingType)
    {
        var parts = new List<string>();

        if (_cache.TryGetValue(FilingExtractionService.RawKey(accession, doc), out string? raw) &&
            !string.IsNullOrEmpty(raw))
            parts.AddRange(FilingSections.Build(raw, FilingSections.ItemsFor(node, filingType)).Select(c => c.Text));

        if (_cache.TryGetValue(FilingExtractionService.ReportsKey(accession, node), out List<FilingChunk>? reports) &&
            reports is not null)
            parts.AddRange(reports.Select(c => c.Text));

        return string.Join("\n", parts);
    }

    /// <summary>Checks whether a verbatim evidence quote occurs in the filing after normalising case,
    /// punctuation and whitespace. Missing or altered words do not pass.</summary>
    private sealed class EvidenceIndex
    {
        private readonly string _flat;

        public EvidenceIndex(string corpus) => _flat = Normalise(corpus);

        public bool Contains(string? evidence)
        {
            var quote = Normalise(evidence ?? "");
            return quote.Length > 0 && _flat.Contains(quote, StringComparison.Ordinal);
        }

        private static string Normalise(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }

}
