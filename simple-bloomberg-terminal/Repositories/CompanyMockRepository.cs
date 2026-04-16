using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

// Java equivalent:
// @Repository
// public class CompanyMockRepository implements ICompanyRepository { ... }
public class CompanyMockRepository : ICompanyRepository
{
    private readonly List<Company> _companies;

    public CompanyMockRepository(ICountryRepository countryRepository)
    {
        // Reuse the country objects from the country repo so navigation properties are shared.
        var usa     = countryRepository.GetById(1)!;
        var germany = countryRepository.GetById(2)!;
        var china   = countryRepository.GetById(3)!;
        var brazil  = countryRepository.GetById(4)!;

        _companies =
        [
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
        ];
    }

    public IEnumerable<Company> GetAll() => _companies;

    public Company? GetById(long id) => _companies.FirstOrDefault(c => c.Id == id);
}
