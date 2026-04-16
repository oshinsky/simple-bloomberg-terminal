namespace simple_bloomberg_terminal.Models.Entities;

public class CountryDetails
{
    public long CountryId { get; set; }
    public string MarketPosition { get; set; } = string.Empty;
    public List<string> Advantages { get; set; } = [];
    public List<string> Challenges { get; set; } = [];
    public List<(int Year, double GdpUsd)> GdpHistory { get; set; } = [];
    public List<(int Year, long Population)> PopHistory { get; set; } = [];
    public List<TradeBloc> TradeBlocs { get; set; } = [];
}
