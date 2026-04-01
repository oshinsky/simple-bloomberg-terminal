using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

public class Company
{
    public Company(string name, long countryId, Sector sector)
    {
        Name = name;
        CountryId = countryId;
        Sector = sector;
    }

    public long Id { get; set; }
    public string Name { get; set; }
    public string? Cik { get; set; }
    public long CountryId { get; set; }
    public Sector Sector { get; set; }
    public GicsIndustry? Industry { get; set; }
    public double? RevenueTotal { get; set; }
    public double? GrossMargin { get; set; }
    public DateOnly? AsOf { get; set; }
    public string? Notes { get; set; }

    public Country? Country { get; set; }
    public List<RevenueSource> RevenueSources { get; set; } = [];
    public List<CostSource> CostSources { get; set; } = [];
    public List<RevenueSource> RevenueFromDependents { get; set; } = [];
    public List<CostSource> CostFromDependents { get; set; } = [];
    public List<Event> Events { get; set; } = [];
}
