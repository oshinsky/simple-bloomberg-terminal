using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

// Java equivalent:
// public interface ICountryDetailsRepository {
//     Optional<CountryDetails> getByCountryId(long countryId);
// }
public interface ICountryDetailsRepository
{
    CountryDetails? GetByCountryId(long countryId);
}
