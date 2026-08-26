using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Extraction;

// Shared filing-worker contract for the two relationship nodes. The node supplies direction, so the
// model only has to identify a named company and prove the commercial relationship.
public static class CounterpartyPrompts
{
    public const string Version = "directional-counterparty-worker-v2";

    public static string FastWorkerSystemPrompt(ExtractionNode node, bool strict = false)
    {
        var classification = node == ExtractionNode.REVENUE ? "CUSTOMER" : "SUPPLIER";
        var direction = node switch
        {
            ExtractionNode.COST =>
                "Find only COST-SIDE counterparties: named suppliers, vendors, manufacturers, foundries, " +
                "contract producers, licensors, or service providers from which the filer buys goods, " +
                "rights, or services. Set classification to SUPPLIER.",
            ExtractionNode.REVENUE =>
                "Find only REVENUE-SIDE counterparties: named customers, buyers, licensees, distributors, " +
                "resellers, or commercial partners through which the filer earns or expects to earn revenue. " +
                "Set classification to CUSTOMER.",
            _ => throw new ArgumentOutOfRangeException(nameof(node), node, "Counterparty prompts apply only to COST and REVENUE.")
        };

        return
            "You extract NAMED COUNTERPARTIES from one plain-text excerpt of a US public company's SEC filing. " +
            direction + " A counterparty must be a named company and the excerpt must establish an explicit " +
            "commercial relationship. Do not use outside knowledge. Do not return business segments, products, " +
            "regions, industries, unnamed customer or supplier concentrations, competitors, litigation adversaries, " +
            "acquisition targets, or companies merely mentioned without the required relationship. Do not derive " +
            "values or percentages from financial tables or company-wide figures. A relationship with no stated " +
            "amount is valid. For every result, write evidence first as one verbatim substring that names the " +
            "counterparty and establishes the relationship. Then return name exactly as written, related_company " +
            "as the same name, the required classification, and a short note describing the relationship. Reply " +
            $"with JSON only: {{\"sources\":[{{\"evidence\":\"\",\"name\":\"\",\"related_company\":\"\"," +
            $"\"classification\":\"{classification}\",\"note\":\"\"}}]}}. If the excerpt establishes no matching " +
            "counterparty, reply {\"sources\":[]}." +
            (strict
                ? " STRICT MODE: require the excerpt itself to state the purchase, sale, supply, license, " +
                  "distribution, resale, or concrete collaboration. A product mention, compatibility statement, " +
                  "industry list, or description of a company as a market leader is insufficient. If uncertain, omit it."
                : "");
    }
}
