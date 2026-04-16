using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

// Java equivalent:
// @Repository
// public class EventMockRepository implements IEventRepository { ... }
//
// Live/past logic (as of 2026-04-16):
//   live : Date <= today AND (EndDate == null OR EndDate >= today)
//   past : Date <  today AND  EndDate != null AND EndDate < today
//
// Apple Q4 Earnings  → past  (EndDate = Date + 1 day = 2024-11-01)
// Fed Rate Decision  → past  (EndDate = Date + 1 day = 2024-11-08)
// US-China Tariffs   → live  (no EndDate)
// EU Carbon Tax      → live  (no EndDate)
// Nvidia Sanctions   → live  (no EndDate)
public class EventMockRepository : IEventRepository
{
    private readonly List<Event> _events;

    public EventMockRepository(ICountryRepository countryRepository, ICompanyRepository companyRepository)
    {
        var usa    = countryRepository.GetById(1)!;
        var china  = countryRepository.GetById(3)!;
        var germany = countryRepository.GetById(2)!;

        var apple  = companyRepository.GetById(1)!;
        var nvidia = companyRepository.GetById(10)!;

        _events =
        [
            new("Apple Q4 Earnings", EventType.EARNINGS, new DateOnly(2024, 10, 31))
                { Id = 1, ImpactScore = 7.5, EndDate = new DateOnly(2024, 11, 1), Companies = [apple] },

            new("Fed Rate Decision", EventType.CENTRAL_BANK, new DateOnly(2024, 11, 7))
                { Id = 2, ImpactScore = 9.1, EndDate = new DateOnly(2024, 11, 8), Countries = [usa] },

            new("US-China Trade Tariffs", EventType.TRADE_DEAL, new DateOnly(2025, 2, 4))
                { Id = 3, ImpactScore = 8.8, Countries = [usa, china] },

            new("EU Carbon Tax Update", EventType.MACRO_DATA, new DateOnly(2025, 1, 15))
                { Id = 4, ImpactScore = 6.2, Countries = [germany] },

            new("Nvidia GPU Export Sanctions", EventType.SANCTIONS, new DateOnly(2025, 3, 1))
                { Id = 5, ImpactScore = 9.4, Companies = [nvidia], Countries = [china] },
        ];
    }

    public IEnumerable<Event> GetAll() => _events;

    public Event? GetById(long id) => _events.FirstOrDefault(e => e.Id == id);
}
