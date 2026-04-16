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

        var apple      = companyRepository.GetById(1)!;
        var nvidia     = companyRepository.GetById(10)!;
        var byd        = companyRepository.GetById(6)!;
        var alibaba    = companyRepository.GetById(7)!;
        var volkswagen = companyRepository.GetById(4)!;
        var vale       = companyRepository.GetById(9)!;
        var exxon      = companyRepository.GetById(3)!;

        _events =
        [
            new("Apple Q4 Earnings", EventType.EARNINGS, new DateOnly(2024, 10, 31))
                { Id = 1, ImpactScore = 7.5, EndDate = new DateOnly(2024, 11, 1), Companies = [apple] },

            new("Fed Rate Decision", EventType.CENTRAL_BANK, new DateOnly(2024, 11, 7))
                { Id = 2, ImpactScore = 9.1, EndDate = new DateOnly(2024, 11, 8), Countries = [usa] },

            new("US-China Trade Tariffs", EventType.TRADE_DEAL, new DateOnly(2025, 2, 4))
            {
                Id = 3, ImpactScore = 8.8, Countries = [usa, china], Companies = [apple, byd, alibaba, volkswagen],
                Description = "The U.S. escalated its tariff campaign against China beginning in February 2025, with cumulative levies reaching 145% on most Chinese imports by April 2025 following a series of staged increases. China retaliated with tariffs of up to 125% on U.S. goods, disrupting bilateral trade flows estimated at over $660B annually. Markets have priced in sustained supply chain restructuring, with multinational earnings guidance reflecting material cost headwinds."
            },

            new("EU Carbon Tax Update", EventType.MACRO_DATA, new DateOnly(2025, 1, 15))
            {
                Id = 4, ImpactScore = 6.2, Countries = [germany], Companies = [vale, exxon, volkswagen],
                Description = "The EU's Carbon Border Adjustment Mechanism entered its full enforcement phase in January 2025, requiring importers of steel, aluminum, cement, fertilizers, electricity, and hydrogen to purchase carbon certificates aligned with the EU ETS price, currently trading near €60–65/tonne. The mechanism targets roughly €2.5B in annual carbon leakage prevention and applies to goods from non-ETS jurisdictions. Affected trading partners including Turkey, India, and Ukraine face compliance costs that analysts estimate could reduce EU import competitiveness in covered sectors by 5–15%."
            },

            new("Nvidia GPU Export Sanctions", EventType.SANCTIONS, new DateOnly(2025, 3, 1))
            {
                Id = 5, ImpactScore = 9.4, Companies = [nvidia], Countries = [china],
                Description = "The U.S. Commerce Department imposed new export controls in March 2025 restricting Nvidia's H20 chip — previously designed to comply with earlier restrictions — from sale to China without a license, effectively closing the last major compliant GPU export channel. Nvidia disclosed a ~$5.5B inventory and purchase obligation charge in April 2025 as a direct result. The restrictions are part of a broader effort to deny China access to AI-capable silicon above defined performance thresholds, with enforcement expanding to cover third-country transshipment routes."
            },
        ];

        // Wire reverse navigation: company.Events ← event
        // Java equivalent: manually maintaining both sides of a @ManyToMany relationship
        foreach (var ev in _events)
            foreach (var company in ev.Companies)
                company.Events.Add(ev);
    }

    public IEnumerable<Event> GetAll() => _events;

    public Event? GetById(long id) => _events.FirstOrDefault(e => e.Id == id);
}
