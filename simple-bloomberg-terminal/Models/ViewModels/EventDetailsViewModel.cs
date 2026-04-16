using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

// Java: record EventDetailsViewModel(Event event, boolean isLive, boolean isPast, String statusLabel, String typeLabel)
// C#: class with required properties; bool is value type (no Optional<Boolean> needed)
public class EventDetailsViewModel
{
    public required Event Event { get; set; }

    // Date <= Today AND (EndDate == null OR EndDate >= Today)
    public required bool IsLive { get; set; }

    // EndDate != null AND EndDate < Today
    public required bool IsPast { get; set; }

    // "LIVE" / "PAST" / "UPCOMING"
    public required string StatusLabel { get; set; }

    // EventType enum formatted for display (underscores → spaces)
    public required string TypeLabel { get; set; }
}
