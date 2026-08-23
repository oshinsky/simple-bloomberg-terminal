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
    string Counterparty, string? Direction, string? What, double? Value, string? Evidence, string? Section);

/// <summary>What one run cost and produced at each layer — the fast model's chunk/finding counts
/// alongside the strong model's item count, so the two can be graded separately.</summary>
/// <param name="Errors">Chunks whose worker call failed or returned unparseable JSON. A failed worker
/// is swallowed by ScanChunkAsync and yields an empty list — correct for production, poison for
/// measurement, because it is indistinguishable from an honest "nothing here" and quietly deflates
/// the run's yield. Counting it is what lets a clean run be told apart from a degraded one.</param>
public record RunStats(
    int Run, int Chunks, int WorkerItems, int LeadItems, int Errors,
    double WorkerGroundPct = 0, double LeadGroundPct = 0);

/// <summary>One CSV line: a single claim from one layer of one run, with the run- and filing-level
/// figures denormalised onto it so a single sheet answers every question.</summary>
public record LedgerRow(
    string Layer, string Company, string Cik, string Accession, string Doc, int Run,
    string Counterparty, string? Direction, string? What, double? Value, string? Evidence, string? Section,
    bool Grounded, double MatchScore,
    int RunsPresent, bool ValueStable, int WhatVariants, int SectionCandidates,
    int RunChunks, int RunWorkerItems, int RunLeadItems, int RunErrors,
    double WorkerGroundPct, double WorkerRepeatPct, double LeadGroundPct, double LeadRepeatPct,
    double RetentionPct, int TotalErrors, string Model, DateTime RunAt);

/// <summary>The paper-facing row: one filing, both layers. Precision is absent by design — it is the
/// manual pass, computed from the annotated `judgement` column.</summary>
public record FilingMeasurement(
    string Company, string Cik, string Accession, int Runs,
    double MeanChunks, double MeanWorkerItems, double MeanLeadItems,
    double WorkerGroundPct, double WorkerRepeatPct,
    double LeadGroundPct, double LeadRepeatPct, double RetentionPct, int TotalErrors,
    string Model, DateTime RunAt,
    IReadOnlyList<RunStats> RunDetail, IReadOnlyList<LedgerRow> Rows, string? Error = null);

/// <summary>
/// Measurement harness for the COST extraction pipeline. Runs the SAME extraction of the SAME filing
/// N times and scores both layers separately:
///
/// <list type="bullet">
/// <item><b>Repeatability</b> — in how many of the N runs each counterparty appeared, and whether its
/// value was identical throughout. Variation in the free-text `what` is counted separately
/// (<c>WhatVariants</c>) and never as instability. Scored per layer.</item>
/// <item><b>Groundedness</b> — whether each claim's `evidence` really occurs in the filing text,
/// after whitespace normalisation, at a 0.90 match threshold. Scored per layer.</item>
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

    // Whitespace-normalised token overlap at or above this ratio counts the evidence as grounded.
    private const double GroundingThreshold = 0.90;

    // Concurrent full-pipeline runs. Each run fans its workers out 6-wide (ScanChunksAsync), so this
    // is the multiplier on worker concurrency: 10 runs => up to 60 simultaneous fast calls. DeepSeek
    // limits CONCURRENT CONNECTIONS rather than requests/minute — 2,500 on the fast tier and 500 on
    // the strong one — so 60 fast + 10 strong sits at a couple of percent of budget. Note the ceiling
    // is a property of the routed provider, not of this code: IChatLlm resolves the user's chosen
    // ParsingProvider, and a tier-limited provider (OpenAI, Anthropic) would need this much lower.
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
            companyId, accession, doc, node, filingType, 1, null, sectionCandidates, keys, onProgress, ct);

        // The rest go WIDE. They no longer contend on the findings cache either, because each lead
        // call is handed its own scan's digest instead of resolving it from that shared key.
        using var gate = new SemaphoreSlim(MaxParallelRuns);
        var rest = await Task.WhenAll(
            Enumerable.Range(2, Math.Max(0, runs - 1)).Select(run => RunPipelineAsync(
                companyId, accession, doc, node, filingType, run, gate, sectionCandidates,
                keys, onProgress, ct)));

        var perRun = rest.Prepend(first).ToArray();
        var stats = perRun.Select(r => r.Stats).OrderBy(s => s.Run).ToList();
        var claims = perRun
            .SelectMany(r => r.Worker.Select(w => (Layer: Worker, r.Stats.Run, Item: w))
                .Concat(r.Lead.Select(l => (Layer: Lead, r.Stats.Run, Item: l))))
            .ToList();

        // The corpus is the filing text, identical for every run; building it after the runs means
        // the raw document and the rendered reports are certain to be in cache.
        var index = new TokenIndex(BuildCorpus(accession, doc, node, filingType));
        // Identity for repeatability is (direction, normalised counterparty) — raw string equality
        // would score "Acme Foundry" and "Acme Foundry Ltd." as two different companies and understate
        // stability for what is plainly one relationship.
        var keyed = claims.Select(c => (c.Layer, c.Run, c.Item, Key: KeyOf(c.Item))).ToList();

        // Scored BY POSITION, not into a dictionary keyed on the claim: a run can legitimately list
        // the same counterparty twice with identical fields, and records compare by value, so a keyed
        // lookup would throw on the duplicate. Distinct quotes are scored once — the same evidence
        // recurs across runs and across the two layers.
        var cachedScores = new Dictionary<string, double>(StringComparer.Ordinal);
        var scores = keyed
            .Select(x => x.Item.Evidence is { } e
                ? cachedScores.TryGetValue(e, out var s) ? s : cachedScores[e] = index?.Score(e) ?? 0
                : 0)
            .ToList();

        // Per layer: a counterparty group is stable when it appears in EVERY run of that layer.
        var groups = keyed
            .Select((x, i) => (x, i))
            .GroupBy(t => (t.x.Layer, t.x.Key))
            .ToDictionary(g => g.Key, g => g.Select(t => t.i).ToList());

        // run: null pools every run (the filing-level figure); a value scores that run alone.
        double GroundOf(string layer, int? run = null)
        {
            var idx = keyed.Select((x, i) => (x, i))
                .Where(t => t.x.Layer == layer && (run is null || t.x.Run == run))
                .Select(t => t.i).ToList();
            return idx.Count == 0 ? 0 : 100.0 * idx.Count(i => scores[i] >= GroundingThreshold) / idx.Count;
        }
        double RepeatOf(string layer)
        {
            var g = groups.Where(kv => kv.Key.Layer == layer).ToList();
            return g.Count == 0
                ? 0
                : 100.0 * g.Count(kv => kv.Value.Select(i => keyed[i].Run).Distinct().Count() == runs) / g.Count;
        }

        // Groundedness IS meaningful per run (each claim is scored on its own), unlike repeatability,
        // which only exists across runs. Fold it back in so the per-run table can show it.
        stats = stats
            .Select(s => s with
            {
                WorkerGroundPct = Math.Round(GroundOf(Worker, s.Run), 1),
                LeadGroundPct = Math.Round(GroundOf(Lead, s.Run), 1),
            })
            .ToList();

        var totalErrors = stats.Sum(s => s.Errors);
        var meanChunks = stats.Count == 0 ? 0 : stats.Average(s => s.Chunks);
        var meanWorker = stats.Count == 0 ? 0 : stats.Average(s => s.WorkerItems);
        var meanLead = stats.Count == 0 ? 0 : stats.Average(s => s.LeadItems);
        var workerGround = Math.Round(GroundOf(Worker), 1);
        var workerRepeat = Math.Round(RepeatOf(Worker), 1);
        var leadGround = Math.Round(GroundOf(Lead), 1);
        var leadRepeat = Math.Round(RepeatOf(Lead), 1);
        // How much of the fast layer's output the strong layer carried through. Recall is the hole in
        // the metric set — groundedness and precision both only see what WAS reported, never what was
        // silently dropped — and this is the cheapest available proxy for it.
        var retention = meanWorker == 0 ? 0 : Math.Round(100.0 * meanLead / meanWorker, 1);

        var byRun = stats.ToDictionary(s => s.Run);
        var rows = keyed.Select((x, i) =>
        {
            var g = groups[(x.Layer, x.Key)];
            var st = byRun[x.Run];
            return new LedgerRow(
                x.Layer, name, cik, accession, doc, x.Run,
                x.Item.Counterparty, x.Item.Direction, x.Item.What, x.Item.Value, x.Item.Evidence, x.Item.Section,
                scores[i] >= GroundingThreshold, Math.Round(scores[i], 3),
                g.Select(j => keyed[j].Run).Distinct().Count(),
                g.Select(j => keyed[j].Item.Value).Distinct().Count() == 1,
                g.Select(j => Norm(keyed[j].Item.What ?? "")).Distinct().Count(),
                x.Item.Section is { } sec ? sectionCandidates.GetValueOrDefault((x.Run, sec)) : 0,
                st.Chunks, st.WorkerItems, st.LeadItems, st.Errors,
                workerGround, workerRepeat, leadGround, leadRepeat, retention, totalErrors,
                model, at);
        }).ToList();

        return new FilingMeasurement(
            name, cik, accession, runs,
            Math.Round(meanChunks, 2), Math.Round(meanWorker, 2), Math.Round(meanLead, 2),
            workerGround, workerRepeat, leadGround, leadRepeat, retention, totalErrors,
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
        int run, SemaphoreSlim? gate, Dictionary<(int, string), int> sectionCandidates,
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
                        onProgress?.Invoke(new MeasureProgress(run, "chunk-error", ChunkIndex: p.Index));
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
            }, ct);

            onProgress?.Invoke(new MeasureProgress(run, "scan-done", WorkerItems: workerItems.Count));

            var lead = await RunOnceAsync(chat, companyId, accession, doc, node, filingType, scanned.Digest, ct);

            onProgress?.Invoke(new MeasureProgress(run, "lead-done", LeadItems: lead.Count));

            lock (sectionCandidates)
                foreach (var kv in chunkFound)
                {
                    var key = (run, chunkItem.TryGetValue(kv.Key, out var it) ? $"Item {it}" : "?");
                    sectionCandidates[key] = sectionCandidates.GetValueOrDefault(key) + kv.Value;
                }

            return (new RunStats(run, scanned.Scanned, workerItems.Count, lead.Count, errors),
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
                LlmJson.Num(el, "value"), LlmJson.Str(el, "evidence"), section);
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
                LlmJson.Num(el, "value"), LlmJson.Str(el, "evidence"), LlmJson.Str(el, "section")));
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
    // Leaving (2) out scored every financial-statement quote as ungrounded even when it was verbatim
    // and correct, because the text simply was not in the file being searched.
    //
    // Empty when nothing is cached — groundedness then scores 0 for everything, which is visible in
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

    /// <summary>
    /// Evidence matcher. A verbatim quote should be a literal substring, so containment is tried first
    /// and settles most items outright. When it misses (the model re-wrapped a line, dropped a
    /// footnote marker, changed a dash), the fallback is token overlap over a same-length window.
    ///
    /// The window is not slid across the whole document: at ~500k tokens that is far too slow for a
    /// per-item check. Instead the RAREST evidence token anchors the search — a distinctive word like
    /// a company name occurs a handful of times, so only a handful of alignments are ever scored.
    /// </summary>
    private sealed class TokenIndex
    {
        private readonly string[] _tokens;
        private readonly Dictionary<string, List<int>> _positions = new();
        private readonly string _flat;
        private const int MaxAnchors = 2000;

        public TokenIndex(string corpus)
        {
            _tokens = Tokenize(corpus);
            _flat = string.Join(' ', _tokens);
            for (int i = 0; i < _tokens.Length; i++)
            {
                if (!_positions.TryGetValue(_tokens[i], out var list))
                    _positions[_tokens[i]] = list = new List<int>();
                list.Add(i);
            }
        }

        public double Score(string? evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence) || _tokens.Length == 0) return 0;
            var ev = Tokenize(evidence);
            if (ev.Length == 0) return 0;

            if (_flat.Contains(string.Join(' ', ev), StringComparison.Ordinal)) return 1.0;

            // Anchor on the evidence token with the fewest occurrences in the corpus; a token absent
            // from the corpus can never align, so it is skipped as an anchor (it still counts against
            // the ratio below, which is what makes a fabricated quote score low).
            int anchor = -1, best = int.MaxValue;
            for (int i = 0; i < ev.Length; i++)
                if (_positions.TryGetValue(ev[i], out var p) && p.Count < best) { best = p.Count; anchor = i; }
            if (anchor < 0) return 0;

            double top = 0;
            foreach (var pos in _positions[ev[anchor]].Take(MaxAnchors))
            {
                int start = pos - anchor;
                if (start < 0 || start + ev.Length > _tokens.Length) continue;
                int hit = 0;
                for (int k = 0; k < ev.Length; k++)
                    if (_tokens[start + k] == ev[k]) hit++;
                top = Math.Max(top, (double)hit / ev.Length);
                if (top >= 1.0) break;
            }
            return top;
        }

        // Whitespace normalisation, per the measurement definition: case-folded alphanumeric tokens,
        // so line wrapping, double spaces and punctuation differences never count as a mismatch.
        private static string[] Tokenize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
    }

}
