using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction.Chat;

// Provides live chat over one SEC filing and emits structured save blocks for form prefilling.
// The client resends visible turns while private filing context is rebuilt on the server.
public interface IExtractionChatService
{
    IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);
}
