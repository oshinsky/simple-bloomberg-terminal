using System.Text;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

public class FilingSectionTests
{
    [Fact]
    public void ItemsFor_RoutesRelationshipAndRiskItems()
    {
        Assert.Equal(["1", "1A", "7", "8"], FilingSections.ItemsFor(ExtractionNode.REVENUE));
        Assert.Equal(["1", "7", "8"], FilingSections.ItemsFor(ExtractionNode.COST));
        Assert.Equal(["1A", "7A"], FilingSections.ItemsFor(ExtractionNode.RISK));
    }

    [Fact]
    public void Build_FlattensHtmlWithoutPreservingTableMarkup()
    {
        const string raw = """
            <html><body>
            <p>Item 7. Management's Discussion and Analysis</p>
            <p>Microsoft purchases cloud services from us.</p>
            <table><tr><td>Customer</td><td>Revenue</td></tr><tr><td>Microsoft</td><td>100</td></tr></table>
            <p>Item 8. Financial Statements</p>
            </body></html>
            """;

        var text = string.Join("\n", FilingSections.Build(raw, ["7"]).Select(chunk => chunk.Text));

        Assert.Contains("Microsoft purchases cloud services from us.", text);
        Assert.Contains("Customer Revenue", text);
        Assert.DoesNotContain("<table", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<td", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_GivesEveryRoutedItemItsShareOfTheChunkBudget()
    {
        var items = FilingSections.ItemsFor(ExtractionNode.REVENUE);
        var paragraph = new string('x', FilingSections.MaxChunkChars / 2 + 100);
        var filing = new StringBuilder();
        foreach (var item in items)
        {
            filing.Append("Item ").Append(item).Append(". Section heading.\n\n");
            for (var i = 0; i < 15; i++) filing.Append(paragraph).Append("\n\n");
        }

        var chunks = FilingSections.Build(filing.ToString(), items);
        var perItem = items.ToDictionary(item => item, item => chunks.Count(c => c.Item == $"Item {item}"));

        Assert.All(perItem, pair => Assert.True(pair.Value > 1, $"Item {pair.Key} was starved."));
        Assert.Single(perItem.Values.Distinct());
    }

    [Fact]
    public void Build_DoesNotTreatCurrentReportItemsAsAnnualItems()
    {
        const string currentReport = "Item 8.01 Other Events.\n\nRevenue increased during the quarter.";

        Assert.Empty(FilingSections.Build(currentReport, FilingSections.ItemsFor(ExtractionNode.REVENUE)));
    }

    [Fact]
    public void Build_FindsSectionByCanonicalTitle_WhenItemNumberIsOmitted()
    {
        const string raw = """
            <html><body>
            <p>Risk Factors</p>
            <p>Supply chain disruption could materially affect results.</p>
            <p>Management's Discussion and Analysis</p>
            <p>Revenue grew during the period.</p>
            </body></html>
            """;

        var risk = string.Join("\n", FilingSections.Build(raw, ["1A"]).Select(c => c.Text));

        Assert.Contains("Supply chain disruption", risk);
        Assert.DoesNotContain("Revenue grew", risk);
    }

    [Fact]
    public void OversizedParagraph_IsSplitWithoutLosingItsTail()
    {
        const string tail = "NAMED CUSTOMER TAIL";
        var raw = "Item 7. Management's Discussion and Analysis.\n\n" +
                  new string('x', FilingSections.MaxChunkChars * 2) + tail +
                  "\n\nItem 8. Financial Statements.";

        var chunks = FilingSections.Build(raw, ["7"]);

        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= FilingSections.MaxChunkChars));
        Assert.Contains(chunks, chunk => chunk.Text.Contains(tail));
    }
}
