using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Extraction;

public enum FastWorkerChunkPhase { Planned, Running, Done, Error }

// Identifies one filing chunk shown in scan progress.
public record FastWorkerChunkInfo(int Index, string Item, IReadOnlyList<string> Titles);

// Reports the chunk plan, worker state, prompt, and response to the UI or harness.
public record FastWorkerScanProgress(
    FastWorkerChunkPhase Phase, int Index, int Found, IReadOnlyList<FastWorkerChunkInfo>? Plan,
    string? Prompt = null, string? Response = null);

public class FastWorkerScanService : IFastWorkerScanService
{
    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly IChatLlm _llm;
    private readonly IMemoryCache _cache;

    private const int MaxParallelFastWorkers = 6;
    private const int FastWorkerMaxTokens = 16_000;
    private const int FastWorkerRetryMaxTokens = 32_000;
    private const int MinHeadingsPerItem = 5;
    private const int MinChunksPerThinItem = 6;

    public FastWorkerScanService(
        ICompanyRepository companies, IStockApiClient client, IChatLlm llm, IMemoryCache cache)
    {
        _companies = companies;
        _client = client;
        _llm = llm;
        _cache = cache;
    }

    // FilingAnalysisContextService reads this node-specific digest for any lead-agent consumer.
    private static string FastWorkerDigestKey(string accession, string doc, ExtractionNode node) =>
        $"filing-findings:{node}:{accession}:{doc}";

    public string? GetCachedDigest(string accession, string doc, ExtractionNode node) =>
        _cache.TryGetValue(FastWorkerDigestKey(accession, doc, node), out string? digest) ? digest : null;
    private static string HeadingsKey(string accession, string doc, ExtractionNode node) =>
        $"filing-headings:{node}:{accession}:{doc}";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    // COST and REVENUE share one directional counterparty contract; RISK has its own compact schema.
    private static string FastWorkerPromptFor(ExtractionNode node, bool strictCounterparties = false) =>
        node == ExtractionNode.RISK
            ? RiskPrompts.FastWorkerSystemPrompt
            : CounterpartyPrompts.FastWorkerSystemPrompt(node, strictCounterparties);

    // Flat fallback: fetches the filing, calls FilingSections to build chunks, then runs workers.
    public async Task<IReadOnlyList<ExtractionSuggestion>> ScanFullSectionsAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default)
    {
        var raw = await FetchRawAsync(companyId, accession, doc, ct);
        if (raw is null) return [];
        return await RunFastWorkerAgentsAsync(
            FilingSections.Build(raw, FilingSections.ItemsFor(node)), node, null, false, null, ct);
    }

    // Supplies lead-agent context with a fast-worker digest. Calls RunFastWorkerScanAsync first, then
    // ScanFullSectionsAsync only when the targeted scan finds nothing.
    public async Task<string> GetOrCreateFastWorkerDigestAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(FastWorkerDigestKey(accession, doc, node), out string? cached)) return cached ?? "";

        var scan = await RunFastWorkerScanAsync(companyId, accession, doc, node, ct: ct);
        if (scan.Found > 0 && _cache.TryGetValue(FastWorkerDigestKey(accession, doc, node), out string? scanned))
            return scanned ?? "";

        var fastWorkerFindings = await ScanFullSectionsAsync(companyId, accession, doc, node, ct);
        var fastWorkerDigest = fastWorkerFindings.Count > 0
            ? BuildFastWorkerDigest(fastWorkerFindings, node)
            : "";
        _cache.Set(FastWorkerDigestKey(accession, doc, node), fastWorkerDigest, CacheFor);
        return fastWorkerDigest;
    }

    // Main fast-worker scan entry point. Calls parsing, report, chunking, and worker helpers to build one
    // deterministic scan plan and a digest for the lead agent.
    public async Task<FastWorkerScanResult> RunFastWorkerScanAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        Action<FastWorkerScanProgress>? onProgress = null, bool strictCounterparties = false,
        bool captureArtifacts = false,
        CancellationToken ct = default)
    {
        var headings = await GetOrParseHeadingsAsync(companyId, accession, doc, node, ct);
        var items = FilingSections.ItemsFor(node);

        // Sparse heading detection is unreliable, so those Items use full-section chunks instead.
        var thin = items
            .Where(i => i != "8" && headings.Count(h => h.Section == $"Item {i}") < MinHeadingsPerItem)
            .ToHashSet();

        // PackHeadingsIntoChunks reduces calls; RankChunks applies a reproducible relevance budget.
        var pickedHeadings = headings
            .Where(h => h.Section != "Item 8" && !thin.Contains(h.Section["Item ".Length..]))
            .ToList();
        var chunks = FilingSections.RankChunks(
            PackHeadingsIntoChunks(pickedHeadings), node, FilingSections.MaxScanChunks);

        // Item 8 comes from the primary filing like every other Item. We intentionally do not download
        // or interpret the SEC's separately rendered financial-table reports.
        if (items.Contains("8"))
        {
            if (await FetchRawAsync(companyId, accession, doc, ct) is { } raw)
                chunks.AddRange(FilingSections.BuildSection(raw, "8", node));
        }

        // Fill remaining capacity with ranked chunks from Items whose headings were unreliable.
        if (thin.Count > 0 && await FetchRawAsync(companyId, accession, doc, ct) is { } thinRaw)
        {
            var remaining = Math.Max(0, FilingSections.MaxScanChunks - chunks.Count);
            var perItem = Math.Max(MinChunksPerThinItem, remaining / thin.Count);
            foreach (var item in thin)
                chunks.AddRange(FilingSections.BuildSection(thinRaw, item, node, perItem));
        }

        // Every source shares one hard worker-call budget, including primary-filing Item 8 fallback.
        chunks = FilingSections.RankChunks(chunks, node, FilingSections.MaxScanChunks);

        // Record which parsed headings reached a worker so the UI can show document coverage.
        var keptHeadings = chunks
            .SelectMany(chunk => (chunk.Titles ?? []).Select(title => (chunk.Section, Title: title)))
            .ToHashSet();
        var report = headings
            .Select(h => new ScannedHeading(h.Section, h.Title, keptHeadings.Contains((h.Section, h.Title))))
            .ToList();

        // Publish the complete plan before RunFastWorkerAgentsAsync begins sending progress events.
        onProgress?.Invoke(new FastWorkerScanProgress(FastWorkerChunkPhase.Planned, -1, 0,
            chunks.Select((c, i) => new FastWorkerChunkInfo(i, c.Item, c.Titles ?? [])).ToList()));

        var workerClaims = new List<ExtractionSuggestion>();
        var fastWorkerFindings = chunks.Count > 0
            ? await RunFastWorkerAgentsAsync(
                chunks, node, onProgress, strictCounterparties, workerClaims, ct)
            : [];
        var fastWorkerDigest = fastWorkerFindings.Count > 0
            ? BuildFastWorkerDigest(fastWorkerFindings, node)
            : "";
        _cache.Set(FastWorkerDigestKey(accession, doc, node), fastWorkerDigest, CacheFor);
        var corpus = captureArtifacts
            ? chunks.Select((chunk, index) => new ExtractionChunkArtifact(
                index, chunk.Item, chunk.Titles ?? [], chunk.Text)).ToList()
            : null;
        return new FastWorkerScanResult(
            chunks.Count, fastWorkerFindings.Count, report, fastWorkerDigest, corpus, workerClaims);
    }

    // Calls FetchRawAsync and FilingSections.BuildHeadings; caches parsing shared by repeated runs.
    private async Task<List<FilingHeading>> GetOrParseHeadingsAsync(
        long companyId, string accession, string doc, ExtractionNode node, CancellationToken ct)
    {
        if (_cache.TryGetValue(HeadingsKey(accession, doc, node), out List<FilingHeading>? cached) && cached is not null)
            return cached;
        var raw = await FetchRawAsync(companyId, accession, doc, ct);
        var headings = raw is null
            ? []
            : FilingSections.BuildHeadings(raw, FilingSections.ItemsFor(node));
        _cache.Set(HeadingsKey(accession, doc, node), headings, CacheFor);
        return headings;
    }

    // Shared with the evidence viewer so it can reuse the filing downloaded by this service.
    public static string RawKey(string accession, string doc) => $"filing-raw:{accession}:{doc}";

    // Calls IStockApiClient for the primary EDGAR HTML and caches it for parsing and evidence display.
    private async Task<string?> FetchRawAsync(
        long companyId, string accession, string doc, CancellationToken ct)
    {
        if (_cache.TryGetValue(RawKey(accession, doc), out string? cached)) return cached;
        if (CompanyCik(companyId) is not { } cik) return null;

        var result = await _client.GetFilingDocument(cik, accession.Replace("-", ""), doc);
        if (string.IsNullOrWhiteSpace(result)) return null;
        _cache.Set(RawKey(accession, doc), result, CacheFor);
        return result;
    }

    // Calls ICompanyRepository and formats the CIK required by EDGAR archive requests.
    private string? CompanyCik(long companyId)
    {
        var company = _companies.GetById(companyId);
        return company is null || string.IsNullOrWhiteSpace(company.Cik) ? null : Cik.Trim(company.Cik);
    }

    // Combines nearby headings until the FilingSections size limit to reduce worker calls.
    private static List<FilingChunk> PackHeadingsIntoChunks(IReadOnlyList<FilingHeading> picked)
    {
        var chunks = new List<FilingChunk>();
        string? item = null;
        var titles = new List<string>();
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            chunks.Add(new FilingChunk(item!, sb.ToString(), item!, titles));
            sb = new StringBuilder();
            titles = new List<string>();
        }
        foreach (var h in picked)
        {
            var piece = $"## {h.Title}\n{h.Body}";
            if (sb.Length > 0 && (h.Section != item || sb.Length + piece.Length > FilingSections.MaxChunkChars))
                Flush();
            item = h.Section;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(piece);
            titles.Add(h.Title);
        }
        Flush();
        return chunks;
    }

    // Calls RunFastWorkerAgentAsync in parallel, then merges duplicate source names.
    private async Task<List<ExtractionSuggestion>> RunFastWorkerAgentsAsync(
        IReadOnlyList<FilingChunk> chunks, ExtractionNode node, Action<FastWorkerScanProgress>? onProgress,
        bool strictCounterparties, List<ExtractionSuggestion>? workerClaims, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(MaxParallelFastWorkers);
        var perChunk = await Task.WhenAll(chunks.Select((c, i) =>
            RunFastWorkerAgentAsync(c, i, node, gate, onProgress, strictCounterparties, ct)));
        workerClaims?.AddRange(perChunk.SelectMany(claims => claims));

        var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in perChunk)
            foreach (var s in list)
            {
                if (string.IsNullOrWhiteSpace(s.Name)) continue;
                byName[s.Name] = byName.TryGetValue(s.Name, out var seen) ? MergeSuggestions(seen, s) : s;
            }
        return byName.Values.ToList();
    }

    // Merges two worker findings with the same name, keeping the evidence attached to the chosen value.
    private static ExtractionSuggestion MergeSuggestions(ExtractionSuggestion a, ExtractionSuggestion b)
    {
        var cls = a.Classification ?? b.Classification;
        var value = a.Value ?? b.Value;
        var pct = a.Percentage ?? b.Percentage;
        var related = !string.IsNullOrWhiteSpace(a.RelatedCompany) ? a.RelatedCompany : b.RelatedCompany;
        var note = !string.IsNullOrWhiteSpace(a.Note) ? a.Note : b.Note;

        var figure = a.Value is not null ? a : b.Value is not null ? b : a;
        var evidence = !string.IsNullOrWhiteSpace(figure.Evidence) ? figure.Evidence
            : !string.IsNullOrWhiteSpace(a.Evidence) ? a.Evidence : b.Evidence;

        return a with
        {
            Classification = cls,
            Value = value,
            Percentage = pct,
            RelatedCompany = related,
            Note = note,
            Evidence = evidence,
        };
    }

    // Converts fast-worker findings into the digest used to build the lead-agent context.
    private static string BuildFastWorkerDigest(
        IReadOnlyList<ExtractionSuggestion> fastWorkerFindings, ExtractionNode node)
    {
        var label = node switch
        {
            ExtractionNode.COST => "cost counterparty candidates",
            ExtractionNode.RISK => "risk candidates",
            _                   => "revenue counterparty candidates",
        };
        var sb = new StringBuilder(
            $"PARALLEL-SCAN FINDINGS ({label} the worker agents pulled from the filing):\n");
        foreach (var s in fastWorkerFindings)
        {
            sb.Append("- ").Append(s.Name);
            if (node == ExtractionNode.RISK && s.Classification != null)
                sb.Append(" [").Append(s.Classification).Append(']');
            if (s.Value != null) sb.Append(" | value=").Append(s.Value);
            if (s.Percentage != null) sb.Append(" | pct=").Append(s.Percentage);
            if (!string.IsNullOrWhiteSpace(s.RelatedCompany)) sb.Append(" | counterparty=").Append(s.RelatedCompany);
            if (!string.IsNullOrWhiteSpace(s.Note)) sb.Append(" | note=").Append(s.Note);
            sb.Append(" | from ").Append(s.Section).Append('\n');
            if (!string.IsNullOrWhiteSpace(s.Evidence))
                sb.Append("    evidence: \"").Append(s.Evidence).Append("\"\n");
        }
        return sb.ToString();
    }

    // Calls IChatLlm for one chunk, parses its JSON, and reports fast-worker progress.
    private async Task<List<ExtractionSuggestion>> RunFastWorkerAgentAsync(
        FilingChunk chunk, int index, ExtractionNode node, SemaphoreSlim gate,
        Action<FastWorkerScanProgress>? onProgress, bool strictCounterparties, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        onProgress?.Invoke(new FastWorkerScanProgress(FastWorkerChunkPhase.Running, index, 0, null));
        var system = FastWorkerPromptFor(node, strictCounterparties);
        var prompt = $"Section: {chunk.Section}\n\nExcerpt:\n\"\"\"\n{chunk.Text}\n\"\"\"";
        // Retain the exact request for progress inspection and measurement errors.
        var transcript = $"━━ SYSTEM PROMPT ━━\n{system}\n\n━━ USER PROMPT ━━\n{prompt}";
        try
        {
            var completion = await _llm.CompleteAsync(
                new ChatRequest(system, prompt, FastWorkerMaxTokens, JsonObject: true, Fast: true), ct);
            var answer = completion.Content;
            var found = ParseFastWorkerResponse(answer, chunk.Section, node).ToList();

            // Retry once with a larger budget only when truncation produced no usable JSON.
            var retried = false;
            if (found.Count == 0 && !IsJsonObject(answer) &&
                string.Equals(completion.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                retried = true;
                completion = await _llm.CompleteAsync(
                    new ChatRequest(system, prompt, FastWorkerRetryMaxTokens, JsonObject: true, Fast: true), ct);
                answer = completion.Content;
                found = ParseFastWorkerResponse(answer, chunk.Section, node).ToList();
            }

            // Report malformed output as an error rather than treating it as an empty finding set.
            if (!IsJsonObject(answer) && found.Count == 0)
            {
                var finish = completion.FinishReason is { Length: > 0 } reason
                    ? $"finish_reason={reason}"
                    : "finish_reason unavailable";
                var retry = retried
                    ? $" Retry with maxTokens={FastWorkerRetryMaxTokens} also failed."
                    : "";
                onProgress?.Invoke(new FastWorkerScanProgress(FastWorkerChunkPhase.Error, index, 0, null, transcript,
                    $"Reply was not valid JSON ({finish}).{retry} Raw reply:\n{answer}"));
                return found;
            }

            onProgress?.Invoke(new FastWorkerScanProgress(
                FastWorkerChunkPhase.Done, index, found.Count, null, transcript, answer));
            return found;
        }
        catch (Exception ex) when (
            !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        {
            onProgress?.Invoke(new FastWorkerScanProgress(
                FastWorkerChunkPhase.Error, index, 0, null, transcript, ex.Message));
            return [];
        }
        finally { gate.Release(); }
    }

    private static bool IsJsonObject(string answer)
    {
        using var probe = LlmJson.ParseObject(answer);
        return probe is not null;
    }

    // Calls LlmJson to parse the worker schema and salvage complete items from truncated output.
    private static IEnumerable<ExtractionSuggestion> ParseFastWorkerResponse(
        string answer, string section, ExtractionNode node)
    {
        using var doc = LlmJson.ParseObject(answer, "]}");
        if (doc is null ||
            !doc.RootElement.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in sources.EnumerateArray())
        {
            var name = ReadJsonText(el, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            yield return new ExtractionSuggestion(
                Name: name!,
                Classification: node == ExtractionNode.RISK ? ReadJsonText(el, "classification") : null,
                Value: null,
                Percentage: null,
                RelatedCompany: ReadJsonText(el, "related_company"),
                Section: section,
                Evidence: ReadJsonText(el, "evidence"),
                Note: ReadJsonText(el, "note"));
        }
    }

    // Reads a JSON field as text because providers may return evidence values as strings or numbers.
    private static string? ReadJsonText(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }
}
