using Microsoft.AspNetCore.Mvc;
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
}
