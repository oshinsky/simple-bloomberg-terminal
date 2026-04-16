using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

// Java equivalent:
// @Repository
// public class CountryMockRepository implements ICountryRepository {
//     private static final List<Country> DATA = List.of(...);
//     ...
// }
//
// C# uses a readonly field initialised once — same "static immutable list" pattern.
// AddSingleton means the instance is created once for the app lifetime, matching
// the Java @Singleton / Spring default singleton scope.
public class CountryMockRepository : ICountryRepository
{
    private readonly List<Country> _countries;

    public CountryMockRepository()
    {
        var usa = new Country("US", "United States", "North America", "USD")
            { Id = 1, GdpUsd = 27.36e12, Population = 335_000_000, RiskRating = 1.2 };

        var germany = new Country("DE", "Germany", "Europe", "EUR")
            { Id = 2, GdpUsd = 4.46e12, Population = 84_000_000, RiskRating = 1.5 };

        var china = new Country("CN", "China", "Asia", "CNY")
            { Id = 3, GdpUsd = 17.79e12, Population = 1_400_000_000, RiskRating = 3.1 };

        var brazil = new Country("BR", "Brazil", "South America", "BRL")
            { Id = 4, GdpUsd = 2.08e12, Population = 215_000_000, RiskRating = 4.2 };

        _countries = [usa, germany, china, brazil];
    }

    // Java: public List<Country> getAll() { return Collections.unmodifiableList(DATA); }
    public IEnumerable<Country> GetAll() => _countries;

    // Java: public Optional<Country> getById(long id) { return DATA.stream().filter(...).findFirst(); }
    public Country? GetById(long id) => _countries.FirstOrDefault(c => c.Id == id);
}
