using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

public class EventsController : Controller
{
    private readonly IEventRepository _events;
    private static readonly DateOnly Today = new(2026, 4, 16);

    public EventsController(IEventRepository events)
    {
        _events = events;
    }

    public IActionResult Index()
    {
        IEnumerable<Event> all = _events.GetAll();

        // Live: started but no end date, OR end date is in the future
        IEnumerable<Event> live = all.Where(e =>
            e.Date <= Today && (e.EndDate == null || e.EndDate >= Today));

        // Past: has an end date that already passed
        IEnumerable<Event> past = all.Where(e =>
            e.EndDate != null && e.EndDate < Today);

        ViewData["LiveEvents"] = live;
        ViewData["PastEvents"] = past;

        return View();
    }
}
