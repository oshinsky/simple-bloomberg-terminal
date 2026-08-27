namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

/// <summary>
/// Versioned, measurement-specific prompt contract. Keeping it in the measurement namespace makes the
/// experimental treatment explicit and prevents conversational prompts from silently changing the test.
/// </summary>
public static class MeasurementPrompts
{
    public const string Version = "counterparty-ledger-v2";

    public const string LeadAgentUserPrompt = "Emit the full counterparty ledger for this filing.";

    // Standalone measurement contract. It intentionally does not inherit conversational save-block,
    // save-block or chat instructions from the production UI.
    public const string LeadAgentSystemPrompt =
        "You are the lead financial analyst for a repeatable filing-extraction measurement. " +
        "Parallel workers have scanned one SEC filing and the filing context below contains their " +
        "counterparty findings. Use only that context; do not use " +
        "outside knowledge and never introduce a company absent from the findings. " +
        LeadAgentOutputInstruction;

    public const string LeadAgentOutputInstruction =
        "reply with NOTHING but a fenced block:\n" +
        "```ledger\n" +
        "{\"items\":[{\"evidence\":\"\",\"counterparty\":\"\",\"direction\":\"SUPPLIER\"," +
        "\"what\":\"\",\"section\":\"\"}]}\n```\n" +
        "One item per NAMED counterparty in the findings above — every one of them, not a selection. " +
        "evidence is the VERBATIM quote from the findings naming that counterparty (copy it exactly; " +
        "do not paraphrase, do not shorten mid-word). counterparty is the company name. direction is " +
        "exactly one of SUPPLIER, CUSTOMER, PARTNER. what is a SHORT description of what is bought or " +
        "sold. section is the SEC Item the finding came from. Never include a company that is not in " +
        "the findings above. Return exactly one item per counterparty. If the same counterparty appears " +
        "more than once or under minor variations of the same name, merge those findings into one item " +
        "and keep the clearest verbatim evidence. No prose before or after the block.";
}
