using System.Text;
using simple_bloomberg_terminal.Models.Enums;
using Xunit.Abstractions;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// The chunk-distribution half of the scan: how many worker calls one filing costs, and whether the
/// chunks that survive a budget are the ones carrying named commercial relationships.
///
/// The scan combines ranked heading chunks with full-section fallback chunks when a filing's visual
/// heading outline is too thin. These tests keep that fallback bounded and counterparty-aware.
/// </summary>
public class ChunkBudgetTests
{
    private readonly ITestOutputHelper _out;

    public ChunkBudgetTests(ITestOutputHelper output) => _out = output;

    // ── Synthetic filings ─────────────────────────────────────────────────────────────────────────

    private const string CounterpartyDisclosure = """
        <p>Microsoft is a named customer and purchases cloud services from us under a commercial agreement.</p>
        """;

    // Boilerplate MD&A prose: no figures, no segment words. Sized just over half the chunk budget so
    // no two paragraphs pack together — one paragraph is one chunk, which makes the counts below
    // arithmetic rather than guesswork.
    private static string Boilerplate(int n) =>
        $"Paragraph {n}. " + new string('x', FilingSections.MaxChunkChars / 2 + 100);

    /// <summary>
    /// An Item 7 with no bold headings at all — the Intel shape that sends the Item down feed C.
    /// <paramref name="tableAt"/> is the paragraph index where the counterparty disclosure is placed.
    /// </summary>
    private static string ThinMdna(int paragraphs, int tableAt)
    {
        // Every block on its own source line, the way EDGAR emits it. Without the line breaks ToText
        // collapses the whole prose run into ONE paragraph (it keeps at most one blank line, and a
        // bare </p> yields only a single newline), and Paragraphs then clips it to a single chunk.
        var sb = new StringBuilder("<html>\n<body>\n");
        sb.Append("<p>Item 7. Management's Discussion and Analysis of Financial Condition.</p>\n");
        for (var i = 0; i < paragraphs; i++)
        {
            if (i == tableAt) sb.Append(CounterpartyDisclosure).Append('\n');
            sb.Append("<p>").Append(Boilerplate(i)).Append("</p>\n");
        }
        sb.Append("<p>Item 8. Financial Statements and Supplementary Data.</p>\n");
        sb.Append("</body>\n</html>");
        return sb.ToString();
    }

    // Document-order truncation: what BuildSection did before ranking. Kept here as the baseline the
    // assertions below compare against, so the tests state a DIFFERENCE rather than a bare number.
    private static List<FilingChunk> FirstN(string raw, string item, int take) =>
        FilingSections.BuildSection(raw, item, ExtractionNode.REVENUE, int.MaxValue).Take(take).ToList();

    // ── Ranking: which chunks survive the cut ─────────────────────────────────────────────────────

    [Fact]
    public void RankedTruncation_KeepsTheCounterpartyDisclosureThatDocumentOrderDropped()
    {
        // The disclosure sits at paragraph 50 of 60, past a simple 40-chunk document-order cut.
        var raw = ThinMdna(60, tableAt: 50);

        var before = FirstN(raw, "7", 40);
        var after = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 40);

        _out.WriteLine($"counterparty kept — document order: {before.Any(c => c.Text.Contains("Microsoft"))}");
        _out.WriteLine($"counterparty kept — ranked:         {after.Any(c => c.Text.Contains("Microsoft"))}");

        Assert.DoesNotContain(before, c => c.Text.Contains("Microsoft"));
        Assert.Contains(after, c => c.Text.Contains("Microsoft"));
    }

    [Fact]
    public void RankedTruncation_SurvivesEvenAtASixthOfTheBudget()
    {
        // The budget a thin Item actually gets once Item 8 and the triaged headings have been paid
        // for (MinChunksPerThinItem). Document order at this width keeps the first six paragraphs of
        // boilerplate; ranking still finds the one paragraph with figures in it.
        var raw = ThinMdna(60, tableAt: 50);
        var after = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 6);

        Assert.Equal(6, after.Count);
        Assert.Contains(after, c => c.Text.Contains("Microsoft"));
    }

    [Fact]
    public void RankedTruncation_HandsTheSurvivorsBackInDocumentOrder()
    {
        // Ranking decides WHICH chunks are read, not what order the worker reads them in — a filing
        // read back-to-front would make the digest incoherent for the lead analyst.
        var raw = ThinMdna(60, tableAt: 50);
        var kept = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 10);

        var all = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, int.MaxValue);
        var positions = kept.Select(k => all.FindIndex(a => a.Text == k.Text)).ToList();

        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void RankedTruncation_IsANoOpWhenTheSectionFitsTheBudget()
    {
        // Most filings never hit the cap. They must come through byte-identical and in filing order —
        // the change is only allowed to alter behaviour on the sections that were being truncated.
        var raw = ThinMdna(10, tableAt: 5);

        var ranked = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 40);
        var plain = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, int.MaxValue);

        Assert.Equal(plain.Select(c => c.Text), ranked.Select(c => c.Text));
    }

    // ── The shared ceiling across the three feeds ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]   // the Intel shape: one Item's outline undetectable
    [InlineData(3)]   // the worst case: every narrative Item thin, feed A contributes nothing
    public void SharedCeiling_BoundsTheWorkerCountRegardlessOfHowManyItemsAreThin(int thinItems)
    {
        const int feedAandB = 10;   // representative heading and primary-filing Item 8 chunks
        var raw = ThinMdna(60, tableAt: 50);

        // Before: each thin Item independently took BuildSection's default 40.
        var before = feedAandB + 40 * thinItems;

        // After: the thin feed splits whatever the ceiling has left, with a floor per Item.
        var remaining = Math.Max(0, FilingSections.MaxScanChunks - feedAandB);
        var perItem = Math.Max(6, remaining / thinItems);   // MinChunksPerThinItem
        var after = feedAandB + Enumerable.Range(0, thinItems)
            .Sum(_ => FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, perItem).Count);

        _out.WriteLine($"{thinItems} thin Item(s): {before} calls ({Math.Ceiling(before / 6.0)} rounds) " +
                       $"→ {after} calls ({Math.Ceiling(after / 6.0)} rounds)");

        Assert.True(after < before, $"expected a reduction, got {before} → {after}");
        Assert.True(after <= FilingSections.MaxScanChunks,
            $"the ceiling is {FilingSections.MaxScanChunks} but the scan planned {after} calls");
    }

    [Fact]
    public void SharedCeiling_NeverStarvesAThinItemToNothing()
    {
        // A busy Item 8 can consume the nominal remaining budget. A thin Item 7 must still receive
        // the minimum scan allocation rather than disappearing without trace.
        var raw = ThinMdna(60, tableAt: 50);
        var remaining = Math.Max(0, FilingSections.MaxScanChunks - 48);
        var perItem = Math.Max(6, remaining / 1);

        var chunks = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, perItem);

        Assert.Equal(6, chunks.Count);
        Assert.Contains(chunks, c => c.Text.Contains("Microsoft"));
    }

    // ── Node awareness ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ranking_FollowsTheNode()
    {
        // The same section ranked for two nodes must not produce the same pick, or the keyword lists
        // are doing nothing. A supplier paragraph is COST's; a customer paragraph is REVENUE's.
        var raw = new StringBuilder("<html>\n<body>\n<p>Item 7. Management's Discussion and Analysis.</p>\n")
            .Append("<p>").Append(Boilerplate(0)).Append("</p>\n")
            .Append("<p>We depend on a single supply agreement for wafer purchase obligations, and our ")
            .Append("cost of revenue rose accordingly. ").Append(new string('y', 2000)).Append("</p>\n")
            .Append("<p>").Append(Boilerplate(1)).Append("</p>\n")
            .Append("<p>Our largest customer accounted for a concentration of net sales in the EMEA ")
            .Append("geographic region. ").Append(new string('z', 2000)).Append("</p>\n")
            .Append("<p>Item 8. Financial Statements and Supplementary Data.</p>\n</body>\n</html>")
            .ToString();

        var forCost = FilingSections.BuildSection(raw, "7", ExtractionNode.COST, 1);
        var forRevenue = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 1);

        Assert.Contains("wafer purchase obligations", forCost.Single().Text);
        Assert.Contains("largest customer", forRevenue.Single().Text);
    }
}
