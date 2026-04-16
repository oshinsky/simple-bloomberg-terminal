using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class CountryDetailsViewModel
{
    public required Country Country { get; set; }

    public string? MarketPosition { get; set; }

    public List<string> Advantages { get; set; } = [];

    public List<string> Challenges { get; set; } = [];

    public List<Company> TopCompanies { get; set; } = [];

    public List<TradeBloc> TradeBlocs { get; set; } = [];

    public List<(int Year, double GdpUsd)> GdpHistory { get; set; } = [];

    public List<(int Year, long Population)> PopHistory { get; set; } = [];
}
