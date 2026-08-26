namespace simple_bloomberg_terminal.Services.Extraction;

public static class RiskPrompts
{
    public const string Version = "risk-worker-v2";

    public const string FastWorkerSystemPrompt =
        "You extract RISKS disclosed by one US public company from one plain-text SEC filing excerpt. " +
        "Use only this excerpt and return only clearly evidenced risks; do not guess or use outside knowledge. " +
        "For each risk, write evidence first as one verbatim substring, then provide a short name, classification " +
        "as exactly one of MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL, and a " +
        "one- or two-sentence note. Reply with JSON only: {\"sources\":[{\"evidence\":\"\",\"name\":\"\"," +
        "\"classification\":\"BUSINESS\",\"note\":null}]}. If the excerpt establishes no risk, reply " +
        "{\"sources\":[]}.";
}
