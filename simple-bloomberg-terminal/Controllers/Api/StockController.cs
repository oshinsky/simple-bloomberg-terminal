using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/stock")]
[Authorize]
public class StockController(ICompanyRepository companies, IStockApiClient client) : ControllerBase
{
    [HttpGet("resolve/{ticker}")]
    public async Task<IActionResult> Resolve(string ticker)
    {
        var cik = await client.ResolveCik(ticker);
        return cik is null ? NotFound() : Ok(new { ticker, cik });
    }

    [HttpGet("filings/{companyId:long}")]
    public async Task<IActionResult> Filings(long companyId)
    {
        var company = companies.GetById(companyId);
        if (company is null) return NotFound();
        if (string.IsNullOrWhiteSpace(company.Cik)) return Conflict("Company has no CIK — not an SEC filer.");

        EdgarSubmissions? submissions;
        try { submissions = await client.GetSubmissions(Cik.Pad(company.Cik)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "SEC unreachable.");
        }
        if (submissions?.Filings?.Recent is not { Form: { } forms })
            return UnprocessableEntity("No filings.");

        var cikNoPad = Cik.Trim(company.Cik);
        var recent = submissions.Filings.Recent;
        var list = Enumerable.Range(0, forms.Count).Select(index =>
        {
            var accession = recent.AccessionNumber?.ElementAtOrDefault(index);
            var document = recent.PrimaryDocument?.ElementAtOrDefault(index);
            return new
            {
                form = forms[index],
                filingDate = recent.FilingDate?.ElementAtOrDefault(index),
                reportDate = recent.ReportDate?.ElementAtOrDefault(index),
                accessionNumber = accession,
                primaryDocument = document,
                description = recent.PrimaryDocDescription?.ElementAtOrDefault(index),
                documentUrl = accession is not null && document is not null
                    ? $"https://www.sec.gov/Archives/edgar/data/{cikNoPad}/{accession.Replace("-", "")}/{document}"
                    : null
            };
        }).ToList();
        return Ok(list);
    }

    [HttpGet("filing/{companyId:long}")]
    public async Task<IActionResult> Filing(
        long companyId, [FromQuery] string accession, [FromQuery] string doc)
    {
        var company = companies.GetById(companyId);
        if (company is null) return NotFound();
        if (string.IsNullOrWhiteSpace(company.Cik)) return Conflict("Company has no CIK — not an SEC filer.");
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");

        try
        {
            var body = await client.GetFilingDocument(Cik.Trim(company.Cik), accession.Replace("-", ""), doc);
            return body is null ? NotFound("Filing document not found.") : Content(body, "text/plain");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "SEC unreachable.");
        }
    }
}
