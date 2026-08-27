using System.Runtime.CompilerServices;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction.Chat;

// Conversational adapter over the shared filing-context and lead-agent services.
public sealed class ExtractionChatService : IExtractionChatService
{
    private readonly IFilingAnalysisContextService _context;
    private readonly ILeadAgentRunner _leadAgent;
    private readonly IFastWorkerScanService _scan;

    public ExtractionChatService(
        IFilingAnalysisContextService context,
        ILeadAgentRunner leadAgent,
        IFastWorkerScanService scan)
    {
        _context = context;
        _leadAgent = leadAgent;
        _scan = scan;
    }

    private static string LeadAgentPromptFor(ExtractionNode node) => node switch
    {
        ExtractionNode.COST =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the COUNTERPARTY candidates below, each with the VERBATIM proof text " +
            "they found. Ground every claim in those findings (or the raw excerpts, if findings are " +
            "absent); if something isn't there, say so rather than guessing - never name a company " +
            "that does not appear in the findings. Help the user review and decide which counterparty " +
            "relationships to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific counterparty, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"COGS\",\"value\":null,\"percentage\":null," +
            "\"related_company\":null,\"related_company_ticker\":null,\"reference\":null," +
            "\"evidence\":\"\"}\n```\n" +
            "name is the counterparty's company name. classification is the accounting bucket the " +
            "spend falls in, exactly one of COGS, OPEX, TOTAL_COSTS - use COGS for a supplier of " +
            "goods or production services, OPEX for a supplier of overhead services. Keep value and " +
            "percentage null unless the filing explicitly attributes a figure to this named counterparty. " +
            "related_company is the same counterparty name; when " +
            "it's a publicly traded company you can identify, also set related_company_ticker to its " +
            "stock ticker (else null) so it can be enriched. reference is the verbatim passage (name " +
            "the SEC Item or note, then the source text) this record was drawn from. evidence is ONE " +
            "VERBATIM excerpt substring backing this record - quote enough to identify any figure you " +
            "report. Emit one save block per counterparty the user confirms, alongside your normal reply.",

        ExtractionNode.RISK =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the RISK candidates below, each with the VERBATIM proof text they " +
            "found. Ground every claim in those findings (or the raw excerpts, if findings are " +
            "absent); if something isn't there, say so rather than guessing. Help the user review and " +
            "decide which disclosed risks to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific risk, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"BUSINESS\",\"note\":null,\"reference\":null," +
            "\"evidence\":\"\"}\n```\n" +
            "classification is the risk scope, exactly one of MACROECONOMIC, INDUSTRY, BUSINESS, " +
            "LEGAL_REGULATORY, FINANCIAL, GENERAL. note is one or two sentences summarising the risk; " +
            "use null when not stated. reference is the verbatim passage (name the SEC Item - 1A risk " +
            "factors / 7A market risk - then the source text) this whole risk record was drawn from. " +
            "evidence is ONE VERBATIM excerpt substring backing this record. Emit one save block per " +
            "risk the user confirms, alongside your normal reply.",

        _ =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported named REVENUE COUNTERPARTIES with verbatim proof. Use only those " +
            "findings (or the raw excerpts when findings are absent). Never invent a company, infer a " +
            "relationship from outside knowledge, or turn a segment, product, region, industry, or unnamed " +
            "customer concentration into a source. Review only named customers, buyers, licensees, " +
            "distributors, resellers, and commercial revenue partners. Aggregate financial figures do " +
            "not establish a value for a specific counterparty, so never attach them to one. " +
            "A counterparty relationship with no stated amount is valid. Be concise.\n\n" +
            "When the user wants to SAVE a specific counterparty, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"CUSTOMER\",\"value\":null," +
            "\"percentage\":null,\"related_company\":\"\",\"related_company_ticker\":null," +
            "\"reference\":null,\"evidence\":\"\"}\n```\n" +
            "name and related_company are the counterparty's company name. classification is always " +
            "CUSTOMER. Keep value and percentage null unless the filing explicitly attributes that figure " +
            "to this named counterparty. Set related_company_ticker only when the filing context identifies " +
            "it reliably. reference names the SEC Item or note and includes the source passage. evidence is " +
            "one verbatim excerpt substring that names the company and establishes the commercial relationship. " +
            "Emit one save block per counterparty the user confirms, alongside your normal reply.",
    };

    public async IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var hasFiling = !string.IsNullOrWhiteSpace(accession) && !string.IsNullOrWhiteSpace(doc);
        if (hasFiling &&
            _scan.GetCachedDigest(accession, doc, node) is null)
            yield return new ChatDelta("status", "Scanning the filing with parallel fast worker agents...");

        var filingContext = await _context.BuildAsync(
            companyId, accession, doc, node, scanIfMissing: true, ct: ct);
        var messages = history
            .Select(message => new LlmMessage(
                message.Role == "assistant" ? "assistant" : "user", message.Content))
            .ToList();
        await foreach (var delta in _leadAgent.StreamAsync(
            LeadAgentPromptFor(node), filingContext, messages, ct))
            yield return delta;
    }
}
