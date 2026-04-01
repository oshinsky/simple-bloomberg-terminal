namespace simple_bloomberg_terminal.Models.Entities;

public class TradeBloc
{
    public TradeBloc(string name, string code)
    {
        Name = name;
        Code = code;
    }

    public long Id { get; set; }
    public string Name { get; set; }
    public string Code { get; set; }
    public string? Description { get; set; }
    public DateOnly? FoundedDate { get; set; }

    public List<Country> Countries { get; set; } = [];
    public List<Event> Events { get; set; } = [];
}
