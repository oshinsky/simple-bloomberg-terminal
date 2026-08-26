using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

public class ExtractionPromptTests
{
    [Fact]
    public void RevenueWorker_IsCounterpartyOnly()
    {
        var prompt = CounterpartyPrompts.FastWorkerSystemPrompt(ExtractionNode.REVENUE);

        Assert.Contains("REVENUE-SIDE", prompt);
        Assert.Contains("classification to CUSTOMER", prompt);
        Assert.Contains("Do not return business segments, products", prompt);
        Assert.Contains("no stated amount is valid", prompt);
        Assert.DoesNotContain("scale any", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CostWorker_IsDirectionalAndSupportsStrictMode()
    {
        var prompt = CounterpartyPrompts.FastWorkerSystemPrompt(ExtractionNode.COST, strict: true);

        Assert.Contains("COST-SIDE", prompt);
        Assert.Contains("classification to SUPPLIER", prompt);
        Assert.Contains("STRICT MODE", prompt);
    }

    [Fact]
    public void RiskWorker_KeepsTheRiskSchema()
    {
        Assert.Contains("LEGAL_REGULATORY", RiskPrompts.FastWorkerSystemPrompt);
        Assert.Contains("verbatim substring", RiskPrompts.FastWorkerSystemPrompt);
        Assert.DoesNotContain("counterparty", RiskPrompts.FastWorkerSystemPrompt, StringComparison.OrdinalIgnoreCase);
    }
}
