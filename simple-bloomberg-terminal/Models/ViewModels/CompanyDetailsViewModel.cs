using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

// Java: record CompanyDetailsViewModel(Company company, List<Event> relatedEvents, String sectorLabel, String industryLabel)
// C#: class with required properties and init-only setters via object initializer
public class CompanyDetailsViewModel
{
    public required Company Company { get; set; }

    public IEnumerable<Event> RelatedEvents { get; set; } = [];

    // Sector enum formatted for display (underscores → spaces)
    public required string SectorLabel { get; set; }

    // Industry enum formatted, or "—" if null
    public required string IndustryLabel { get; set; }
}
