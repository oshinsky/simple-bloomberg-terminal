namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

/// <summary>Projects completed pipeline runs into raw rows for comparison and manual review.</summary>
public static class MeasurementCalculator
{
    private const string FastWorker = "FAST_WORKER";
    private const string LeadAgent = "LEAD_AGENT";

    public static CounterpartyMeasurementResult Calculate(
        IReadOnlyList<CounterpartyRunResult> runs,
        string model,
        DateTime runAt)
    {
        if (runs.Count == 0) throw new ArgumentException("At least one run is required.", nameof(runs));

        var ordered = runs.OrderBy(run => run.Run).ToList();
        var target = ordered[0].Target;
        var claims = ordered.SelectMany(run =>
            run.FastWorkerClaims.Select(item => (Layer: FastWorker, run.Run, Item: item))
                .Concat(run.LeadAgentClaims.Select(item => (Layer: LeadAgent, run.Run, Item: item))))
            .ToList();
        var rows = claims.Select(x => new CounterpartyMeasurementRow(
            x.Layer, target.Company, target.Cik, target.Accession, target.Document, x.Run,
            x.Item.Counterparty, x.Item.Direction, x.Item.What, x.Item.Evidence, x.Item.Section)).ToList();
        var totalErrors = ordered.Sum(run => run.Errors.Count);

        return new CounterpartyMeasurementResult(
            target.Company, target.Cik, target.Accession, ordered.Count,
            totalErrors, model, runAt, rows);
    }
}
