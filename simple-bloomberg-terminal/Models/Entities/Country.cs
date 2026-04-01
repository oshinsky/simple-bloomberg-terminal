namespace simple_bloomberg_terminal.Models.Entities;

public class Country
{
    public Country(string code, string name, string region, string currencyCode)
    {
        Code = code;
        Name = name;
        Region = region;
        CurrencyCode = currencyCode;
    }

    public long Id { get; set; }
    public string Code { get; set; }
    public string Name { get; set; }
    public string Region { get; set; }
    public string CurrencyCode { get; set; }
    public double? GdpUsd { get; set; }
    public long? Population { get; set; }
    public double? RiskRating { get; set; }
    public string? Notes { get; set; }

    public List<Company> Companies { get; set; } = [];
    public List<TradeBloc> TradeBlocs { get; set; } = [];
    public List<Event> Events { get; set; } = [];
}
