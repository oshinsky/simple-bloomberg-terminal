using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

// Java equivalent:
// @Repository
// public class CountryDetailsMockRepository implements ICountryDetailsRepository {
//     private static final Map<Long, CountryDetails> DATA = Map.of(...);
//     public Optional<CountryDetails> getByCountryId(long id) { return Optional.ofNullable(DATA.get(id)); }
// }
public class CountryDetailsMockRepository : ICountryDetailsRepository
{
    private readonly Dictionary<long, CountryDetails> _data;

    public CountryDetailsMockRepository()
    {
        // ── TradeBlocs ────────────────────────────────────────────────────────
        var usmca = new TradeBloc("United States–Mexico–Canada Agreement", "USMCA") { Id = 1 };
        var g7    = new TradeBloc("Group of Seven", "G7")  { Id = 2 };
        var g20   = new TradeBloc("Group of Twenty", "G20") { Id = 3 };
        var nato  = new TradeBloc("North Atlantic Treaty Organization", "NATO") { Id = 4 };
        var eu    = new TradeBloc("European Union", "EU")   { Id = 5 };
        var rcep  = new TradeBloc("Regional Comprehensive Economic Partnership", "RCEP") { Id = 6 };
        var sco   = new TradeBloc("Shanghai Cooperation Organisation", "SCO") { Id = 7 };
        var brics = new TradeBloc("BRICS", "BRICS")         { Id = 8 };
        var mercosur = new TradeBloc("Mercado Común del Sur", "Mercosur") { Id = 9 };

        // ── USA (id = 1) ──────────────────────────────────────────────────────
        var usa = new CountryDetails
        {
            CountryId = 1,
            MarketPosition = "Largest economy globally — 25% of world GDP. Reserve currency nation and primary driver of global financial markets.",
            Advantages =
            [
                "Tech dominance — home to the world's largest tech companies (Apple, Microsoft, Nvidia, Google)",
                "USD reserve currency status underpins global trade and gives the US unique borrowing advantages",
                "Deep, liquid capital markets — NYSE and NASDAQ are the world's largest equity exchanges",
                "Military and diplomatic reach provides geopolitical stability and enforces trade agreements",
                "Energy independence achieved through shale revolution — largest oil & gas producer globally",
                "Silicon Valley innovation ecosystem continuously generates world-leading startups and IP",
            ],
            Challenges =
            [
                "Rising national debt ($33T+) — interest payments now exceed defence spending",
                "Widening income inequality erodes consumer demand and fuels social instability",
                "Political polarisation creates policy uncertainty and risks of legislative gridlock",
                "Infrastructure gap — ageing roads, bridges and grid require multi-trillion dollar investment",
            ],
            GdpHistory =
            [
                (2019, 21.37e12),
                (2020, 20.89e12),
                (2021, 23.32e12),
                (2022, 25.46e12),
                (2023, 27.06e12),
                (2024, 27.36e12),
            ],
            PopHistory =
            [
                (2019, 329_000_000),
                (2020, 331_000_000),
                (2021, 332_000_000),
                (2022, 333_000_000),
                (2023, 334_000_000),
                (2024, 335_000_000),
            ],
            TradeBlocs = [usmca, g7, g20, nato],
        };

        // ── Germany (id = 2) ─────────────────────────────────────────────────
        var germany = new CountryDetails
        {
            CountryId = 2,
            MarketPosition = "Largest economy in Europe and 4th globally. Export powerhouse and industrial backbone of the EU.",
            Advantages =
            [
                "Engineering excellence — world-class manufacturing in automotive, machinery and chemicals",
                "Export competitiveness driven by the 'Mittelstand' — highly productive SME industrial base",
                "EU single market access provides a home market of 450M+ consumers",
                "Highly skilled workforce supported by the dual vocational education system",
                "Low corruption and institutional stability attract long-term foreign investment",
            ],
            Challenges =
            [
                "Energy dependency exposed after Nord Stream sabotage — high industrial energy costs",
                "Aging population and shrinking workforce threaten long-run growth potential",
                "Slow digital adoption — lags peers in cloud, e-commerce and fintech adoption",
                "High energy costs hitting energy-intensive industries such as chemicals and steel",
            ],
            GdpHistory =
            [
                (2019, 3.89e12),
                (2020, 3.84e12),
                (2021, 4.26e12),
                (2022, 4.08e12),
                (2023, 4.43e12),
                (2024, 4.46e12),
            ],
            PopHistory =
            [
                (2019, 83_100_000),
                (2020, 83_200_000),
                (2021, 83_200_000),
                (2022, 83_800_000),
                (2023, 84_400_000),
                (2024, 84_000_000),
            ],
            TradeBlocs = [eu, g7, g20, nato],
        };

        // ── China (id = 3) ───────────────────────────────────────────────────
        var china = new CountryDetails
        {
            CountryId = 3,
            MarketPosition = "2nd largest economy globally and largest manufacturing hub. Central player in global supply chains and emerging tech.",
            Advantages =
            [
                "Massive manufacturing scale — 'factory of the world' producing ~28% of global manufactured output",
                "Belt & Road Initiative extends infrastructure investment and geopolitical influence across 140+ countries",
                "Largest population and consumer market — 1.4B people with rapidly growing middle class",
                "Rapid EV and renewable energy adoption positions China as global clean-tech leader",
                "State-backed investment capacity enables long-horizon industrial policy without private-sector constraints",
            ],
            Challenges =
            [
                "Property sector crisis — Evergrande default signals systemic stress in a sector worth ~30% of GDP",
                "US-China trade tensions and tech sanctions restrict access to advanced semiconductors",
                "Demographic decline — shrinking workforce due to legacy one-child policy",
                "Capital controls limit foreign investor confidence and renminbi internationalisation",
                "Geopolitical risks around Taiwan create tail risk for global supply chains",
            ],
            GdpHistory =
            [
                (2019, 14.34e12),
                (2020, 14.69e12),
                (2021, 17.73e12),
                (2022, 17.96e12),
                (2023, 17.52e12),
                (2024, 17.79e12),
            ],
            PopHistory =
            [
                (2019, 1_400_050_000),
                (2020, 1_411_780_000),
                (2021, 1_412_600_000),
                (2022, 1_411_750_000),
                (2023, 1_409_670_000),
                (2024, 1_400_000_000),
            ],
            TradeBlocs = [rcep, sco, g20, brics],
        };

        // ── Brazil (id = 4) ──────────────────────────────────────────────────
        var brazil = new CountryDetails
        {
            CountryId = 4,
            MarketPosition = "Largest economy in South America and 9th globally. Dominant in agricultural commodities and natural resources.",
            Advantages =
            [
                "Agricultural superpower — world's largest exporter of soybeans, beef, sugar and orange juice",
                "Vast natural resources including the world's largest iron ore reserves and pre-salt offshore oil",
                "Biodiversity and ecotourism potential from the Amazon basin provide long-run sustainable revenue",
                "Young and growing population drives domestic consumption and labour supply",
                "Regional geopolitical influence through Mercosur leadership and BRICS membership",
            ],
            Challenges =
            [
                "High interest rates — Selic rate historically 13%+ suppresses investment and growth",
                "Political instability creates policy unpredictability and deters foreign direct investment",
                "Infrastructure bottlenecks in logistics, ports and roads raise commodity export costs",
                "Deforestation risk and ESG pressure from international investors constrains agri expansion",
                "Currency volatility (BRL) exposes exporters and importers to significant FX risk",
            ],
            GdpHistory =
            [
                (2019, 1.87e12),
                (2020, 1.44e12),
                (2021, 1.61e12),
                (2022, 1.92e12),
                (2023, 2.13e12),
                (2024, 2.08e12),
            ],
            PopHistory =
            [
                (2019, 211_050_000),
                (2020, 212_560_000),
                (2021, 213_990_000),
                (2022, 214_930_000),
                (2023, 215_310_000),
                (2024, 215_000_000),
            ],
            TradeBlocs = [mercosur, g20, brics],
        };

        _data = new Dictionary<long, CountryDetails>
        {
            [1] = usa,
            [2] = germany,
            [3] = china,
            [4] = brazil,
        };
    }

    // Java: return Optional.ofNullable(_data.get(countryId));
    public CountryDetails? GetByCountryId(long countryId) =>
        _data.GetValueOrDefault(countryId);
}
