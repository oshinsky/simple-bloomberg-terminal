using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

// ── Seed data ────────────────────────────────────────────────────────────────

var usa = new Country("US", "United States", "North America", "USD")
    { Id = 1, GdpUsd = 27.36e12, Population = 335_000_000, RiskRating = 1.2 };

var germany = new Country("DE", "Germany", "Europe", "EUR")
    { Id = 2, GdpUsd = 4.46e12, Population = 84_000_000, RiskRating = 1.5 };

var china = new Country("CN", "China", "Asia", "CNY")
    { Id = 3, GdpUsd = 17.79e12, Population = 1_400_000_000, RiskRating = 3.1 };

var brazil = new Country("BR", "Brazil", "South America", "BRL")
    { Id = 4, GdpUsd = 2.08e12, Population = 215_000_000, RiskRating = 4.2 };

var companies = new List<Company>
{
    new("Apple Inc.", 1, Sector.INFORMATION_TECHNOLOGY)
        { Id = 1, RevenueTotal = 383e9, GrossMargin = 0.441, Industry = GicsIndustry.TECHNOLOGY_HARDWARE_STORAGE_AND_PERIPHERALS, Country = usa },
    new("Microsoft Corp.", 1, Sector.INFORMATION_TECHNOLOGY)
        { Id = 2, RevenueTotal = 212e9, GrossMargin = 0.689, Industry = GicsIndustry.SOFTWARE, Country = usa },
    new("ExxonMobil", 1, Sector.ENERGY)
        { Id = 3, RevenueTotal = 398e9, GrossMargin = 0.162, Industry = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS, Country = usa },
    new("Volkswagen AG", 2, Sector.CONSUMER_DISCRETIONARY)
        { Id = 4, RevenueTotal = 293e9, GrossMargin = 0.178, Industry = GicsIndustry.AUTOMOBILES, Country = germany },
    new("SAP SE", 2, Sector.INFORMATION_TECHNOLOGY)
        { Id = 5, RevenueTotal = 34e9,  GrossMargin = 0.724, Industry = GicsIndustry.SOFTWARE, Country = germany },
    new("BYD Co.", 3, Sector.CONSUMER_DISCRETIONARY)
        { Id = 6, RevenueTotal = 85e9,  GrossMargin = 0.189, Industry = GicsIndustry.AUTOMOBILES, Country = china },
    new("Alibaba Group", 3, Sector.CONSUMER_DISCRETIONARY)
        { Id = 7, RevenueTotal = 131e9, GrossMargin = 0.381, Industry = GicsIndustry.BROADLINE_RETAIL, Country = china },
    new("Petrobras", 4, Sector.ENERGY)
        { Id = 8, RevenueTotal = 124e9, GrossMargin = 0.512, Industry = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS, Country = brazil },
    new("Vale S.A.", 4, Sector.MATERIALS)
        { Id = 9, RevenueTotal = 42e9,  GrossMargin = 0.431, Industry = GicsIndustry.METALS_AND_MINING, Country = brazil },
    new("Nvidia Corp.", 1, Sector.INFORMATION_TECHNOLOGY)
        { Id = 10, RevenueTotal = 61e9, GrossMargin = 0.731, Industry = GicsIndustry.SEMICONDUCTORS_AND_SEMICONDUCTOR_EQUIPMENT, Country = usa },
};

var events = new List<Event>
{
    new("Apple Q4 Earnings", EventType.EARNINGS, new DateOnly(2024, 10, 31))
        { Id = 1, ImpactScore = 7.5, Companies = [companies[0]] },
    new("Fed Rate Decision", EventType.CENTRAL_BANK, new DateOnly(2024, 11, 7))
        { Id = 2, ImpactScore = 9.1, Countries = [usa] },
    new("US-China Trade Tariffs", EventType.TRADE_DEAL, new DateOnly(2025, 2, 4))
        { Id = 3, ImpactScore = 8.8, Countries = [usa, china] },
    new("EU Carbon Tax Update", EventType.MACRO_DATA, new DateOnly(2025, 1, 15))
        { Id = 4, ImpactScore = 6.2, Countries = [germany] },
    new("Nvidia GPU Export Sanctions", EventType.SANCTIONS, new DateOnly(2025, 3, 1))
        { Id = 5, ImpactScore = 9.4, Companies = [companies[9]], Countries = [china] },
};

Console.WriteLine("=== Bloomberg Terminal – LINQ analiza ===\n");

IEnumerable<Company> europeCompanies = companies
    .Where(c => c.Country?.Region == "Europe")
    .OrderByDescending(c => c.RevenueTotal);

foreach (Company c in europeCompanies)
    Console.WriteLine($"   {c.Name,-25} ${c.RevenueTotal / 1e9:F0}B");

IEnumerable<Event> earningsEvents = events
    .Where(e => e.Type == EventType.EARNINGS)
    .OrderBy(e => e.Date);

Console.WriteLine("\nEarnings događaji:");
foreach (Event e in earningsEvents)
    Console.WriteLine($"   [{e.Date}] {e.Title,-35} impact: {e.ImpactScore:F1}");

IEnumerable<IGrouping<Sector, Company>> companiesPerSector = companies
    .GroupBy(c => c.Sector)
    .OrderByDescending(g => g.Count());

Console.WriteLine("\nBroj kompanija po sektoru:");
foreach (IGrouping<Sector, Company> g in companiesPerSector)
    Console.WriteLine($"   {g.Key,-30} {g.Count()} kompanija");

IEnumerable<IGrouping<Sector, Company>> avgMarginBySector = companies
    .Where(c => c.GrossMargin.HasValue)
    .GroupBy(c => c.Sector)
    .OrderByDescending(g => g.Average(c => c.GrossMargin!.Value));

Console.WriteLine("\nProsječna bruto marža po sektoru:");
foreach (IGrouping<Sector, Company> g in avgMarginBySector)
    Console.WriteLine($"   {g.Key,-30} {g.Average(c => c.GrossMargin!.Value):P1}");

Dictionary<string, double> avgRevenueByRegion = companies
    .Where(c => c.Country != null && c.RevenueTotal.HasValue)
    .GroupBy(c => c.Country!.Region)
    .ToDictionary(g => g.Key, g => g.Average(c => c.RevenueTotal!.Value));

// ── Web aplikacija ────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

// Java equivalent: @Bean / @Component + @Autowired wiring via Spring container.
// AddSingleton = one instance for the full app lifetime (Spring's default singleton scope).
// Order matters: CompanyMockRepository depends on ICountryRepository,
// EventMockRepository depends on both — ASP.NET Core DI resolves the graph automatically.
builder.Services.AddSingleton<ICountryRepository, CountryMockRepository>();
builder.Services.AddSingleton<ICompanyRepository, CompanyMockRepository>();
builder.Services.AddSingleton<IEventRepository, EventMockRepository>();
builder.Services.AddSingleton<ICountryDetailsRepository, CountryDetailsMockRepository>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
