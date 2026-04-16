using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

public class CompaniesController : Controller
{
    private readonly ICompanyRepository _companies;

    public CompaniesController(ICompanyRepository companies)
    {
        _companies = companies;
    }

    public IActionResult Index()
    {
        return View(_companies.GetAll());
    }

    public IActionResult Details(long id)
    {
        var company = _companies.GetById(id);
        if (company == null) return NotFound();

        var vm = new CompanyDetailsViewModel
        {
            Company = company,
            RelatedEvents = company.Events,
            SectorLabel = company.Sector.ToString().Replace("_", " "),
            IndustryLabel = company.Industry.HasValue
                ? company.Industry.Value.ToString().Replace("_", " ")
                : "—"
        };

        return View(vm);
    }
}
