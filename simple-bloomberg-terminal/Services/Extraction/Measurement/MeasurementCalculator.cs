namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

/// <summary>Pure, reproducible scoring over completed pipeline runs; performs no network or model I/O.</summary>
public static class MeasurementCalculator
{
    private const string FastWorker = "FAST_WORKER";
    private const string LeadAgent = "LEAD_AGENT";

    public static CounterpartyMeasurementResult Calculate(
        IReadOnlyList<CounterpartyRunResult> runs,
        string model,
        DateTime runAt,
        IReadOnlyDictionary<(int Run, string Section), int>? sectionCandidates = null)
    {
        if (runs.Count == 0) throw new ArgumentException("At least one run is required.", nameof(runs));

        var ordered = runs.OrderBy(run => run.Run).ToList();
        var target = ordered[0].Target;
        var evidence = new EvidenceIndex(ordered[0].Corpus);
        var claims = ordered.SelectMany(run =>
            run.FastWorkerClaims.Select(item => (Layer: FastWorker, run.Run, Item: item))
                .Concat(run.LeadAgentClaims.Select(item => (Layer: LeadAgent, run.Run, Item: item))))
            .ToList();
        var keyed = claims.Select((claim, index) =>
            (claim.Layer, claim.Run, claim.Item, Index: index, Key: CounterpartyIdentity.Key(claim.Item)))
            .ToList();
        var evidenceFound = keyed.Select(x => evidence.Contains(x.Item.Evidence)).ToArray();
        var groups = keyed.GroupBy(x => (x.Layer, x.Key)).ToDictionary(g => g.Key, g => g.ToList());

        double EvidencePercent(string layer, int? run = null)
        {
            var selected = keyed.Where(x => x.Layer == layer && (run is null || x.Run == run)).ToList();
            return selected.Count == 0 ? 0 : 100.0 * selected.Count(x => evidenceFound[x.Index]) / selected.Count;
        }

        double RepeatPercent(string layer)
        {
            var selected = groups.Where(group => group.Key.Layer == layer).ToList();
            return selected.Count == 0
                ? 0
                : 100.0 * selected.Count(group => group.Value.Select(x => x.Run).Distinct().Count() == ordered.Count) /
                  selected.Count;
        }

        var stats = ordered.Select(run => new ExtractionRunMetrics(
            run.Run,
            run.Corpus.Count,
            run.FastWorkerClaims.Count,
            run.LeadAgentClaims.Count,
            run.Errors.Count,
            Math.Round(EvidencePercent(FastWorker, run.Run), 1),
            Math.Round(EvidencePercent(LeadAgent, run.Run), 1),
            run.Errors)).ToList();
        var meanChunks = stats.Average(x => x.Chunks);
        var meanFastWorkerClaims = stats.Average(x => x.FastWorkerClaims);
        var meanLeadAgentClaims = stats.Average(x => x.LeadAgentClaims);
        var fastWorkerEvidence = Math.Round(EvidencePercent(FastWorker), 1);
        var fastWorkerRepeat = Math.Round(RepeatPercent(FastWorker), 1);
        var leadAgentEvidence = Math.Round(EvidencePercent(LeadAgent), 1);
        var leadAgentRepeat = Math.Round(RepeatPercent(LeadAgent), 1);
        var retention = meanFastWorkerClaims == 0
            ? 0
            : Math.Round(100.0 * meanLeadAgentClaims / meanFastWorkerClaims, 1);
        var totalErrors = stats.Sum(x => x.Errors);
        var byRun = stats.ToDictionary(x => x.Run);

        var rows = keyed.Select(x =>
        {
            var group = groups[(x.Layer, x.Key)];
            var statsForRun = byRun[x.Run];
            var section = x.Item.Section ?? "?";
            return new CounterpartyMeasurementRow(
                x.Layer, target.Company, target.Cik, target.Accession, target.Document, x.Run,
                x.Item.Counterparty, x.Item.Direction, x.Item.What, x.Item.Evidence, x.Item.Section,
                evidenceFound[x.Index],
                group.Select(item => item.Run).Distinct().Count(),
                group.Select(item => CounterpartyIdentity.Normalize(item.Item.What ?? "")).Distinct().Count(),
                sectionCandidates?.GetValueOrDefault((x.Run, section)) ?? 0,
                statsForRun.Chunks, statsForRun.FastWorkerClaims, statsForRun.LeadAgentClaims, statsForRun.Errors,
                fastWorkerEvidence, fastWorkerRepeat, leadAgentEvidence, leadAgentRepeat,
                retention, totalErrors, model, runAt);
        }).ToList();

        return new CounterpartyMeasurementResult(
            target.Company, target.Cik, target.Accession, ordered.Count,
            Math.Round(meanChunks, 2), Math.Round(meanFastWorkerClaims, 2), Math.Round(meanLeadAgentClaims, 2),
            fastWorkerEvidence, fastWorkerRepeat, leadAgentEvidence, leadAgentRepeat, retention, totalErrors,
            model, runAt, stats, rows);
    }
}
