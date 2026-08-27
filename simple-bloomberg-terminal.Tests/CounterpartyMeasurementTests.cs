using simple_bloomberg_terminal.Services.Extraction.Measurement;

namespace simple_bloomberg_terminal.Tests;

public class CounterpartyMeasurementTests
{
    [Fact]
    public void LeadAgentPrompt_ExplicitlyRequiresDuplicateCounterpartiesToBeMerged()
    {
        Assert.Contains("exactly one item per counterparty", MeasurementPrompts.LeadAgentSystemPrompt);
        Assert.Contains("minor variations of the same name", MeasurementPrompts.LeadAgentSystemPrompt);
        Assert.Contains("merge those findings", MeasurementPrompts.LeadAgentSystemPrompt);
    }

    [Fact]
    public void LeadAgentCodec_SalvagesCompleteItemsFromATruncatedReply()
    {
        const string truncated = """
            {"items":[{"evidence":"Acme supplies chips.","counterparty":"Acme",
            "direction":"SUPPLIER","what":"chips","section":"Item 1"},{"evidence":"unfinished
            """;

        var item = Assert.Single(LeadAgentLedgerCodec.Parse(truncated));

        Assert.Equal("Acme", item.Counterparty);
    }

    [Fact]
    public void Calculator_ProjectsRawClaimsAcrossRuns()
    {
        var target = new FilingTarget(1, "Example", "1", "0001", "example.htm", "10-K");
        var corpus = new[]
        {
            new ExtractionChunkArtifact(0, "Item 1", [], "Acme supplies the company with chips.")
        };
        var acme = new CounterpartyClaim("Acme Ltd.", "SUPPLIER", "chips",
            "Acme supplies the company with chips.", "Item 1");
        var runs = new[]
        {
            new CounterpartyRunResult(1, target, corpus, [acme], [acme], []),
            new CounterpartyRunResult(2, target, corpus, [acme], [], [])
        };

        var result = MeasurementCalculator.Calculate(runs, "test/model", DateTime.UnixEpoch);

        Assert.Equal(2, result.Runs);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(2, result.Rows.Count(row => row.Layer == "FAST_WORKER"));
        Assert.Single(result.Rows, row => row.Layer == "LEAD_AGENT");
    }
}
