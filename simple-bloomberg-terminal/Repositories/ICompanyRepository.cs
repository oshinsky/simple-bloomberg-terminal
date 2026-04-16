using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

// Java equivalent:
// public interface ICompanyRepository {
//     List<Company> getAll();
//     Optional<Company> getById(long id);
// }
public interface ICompanyRepository
{
    IEnumerable<Company> GetAll();
    Company? GetById(long id);
}
