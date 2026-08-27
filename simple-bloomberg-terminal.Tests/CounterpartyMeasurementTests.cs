using simple_bloomberg_terminal.Services.Extraction.Measurement;

namespace simple_bloomberg_terminal.Tests;

public class CounterpartyMeasurementTests
{
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
    public void EvidenceIndex_SearchesTheExactFastWorkerCorpusWithNormalizedPunctuation()
    {
        var chunks = new[]
        {
            new ExtractionChunkArtifact(0, "Item 1", ["Suppliers"],
                "We purchase substantially all chips from Acme Foundry, Ltd.")
        };
        var index = new EvidenceIndex(chunks);

        Assert.True(index.Contains("we purchase substantially all chips from ACME FOUNDRY LTD"));
        Assert.False(index.Contains("Acme supplies all of our cloud capacity"));
    }

    [Theory]
    [InlineData("Acme Foundry Ltd.", "acme foundry")]
    [InlineData("ACME Foundry, L.L.C.", "acme foundry")]
    public void IdentityNormalization_RemovesFormattingAndCorporateSuffixes(string input, string expected)
    {
        Assert.Equal(expected, CounterpartyIdentity.Normalize(input));
    }

    [Fact]
    public void Calculator_MeasuresRepeatabilityEvidenceAndRetentionAcrossRuns()
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

        Assert.Equal(100, result.FastWorkerEvidencePct);
        Assert.Equal(100, result.FastWorkerRepeatPct);
        Assert.Equal(50, result.RetentionPct);
        Assert.Equal(2, result.RunDetail.Count);
    }
}
