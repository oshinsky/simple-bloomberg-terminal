using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

// Java equivalent:
// public interface ICountryRepository {
//     List<Country> getAll();
//     Optional<Country> getById(long id);
// }
public interface ICountryRepository
{
    IEnumerable<Country> GetAll();
    Country? GetById(long id);
}
