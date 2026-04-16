using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

// Java equivalent:
// public interface IEventRepository {
//     List<Event> getAll();
//     Optional<Event> getById(long id);
// }
public interface IEventRepository
{
    IEnumerable<Event> GetAll();
    Event? GetById(long id);
}
