using System.Net;
using System.Net.Http.Json;

namespace simple_bloomberg_terminal.Tests;

public class StockTests : ApiTestBase
{
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;

    [Fact]
    public async Task Resolve_KnownTicker_ReturnsCik()
    {
        var response = await Client.GetAsync("/api/stock/resolve/AAPL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResolveResult>();
        Assert.Equal("0000320193", body!.Cik);
    }

    [Fact]
    public async Task Resolve_UnknownTicker_Returns404()
    {
        var response = await Client.GetAsync("/api/stock/resolve/ZZZZ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Filings_AppleWithCik_ListsFilingsWithDocumentUrl()
    {
        var response = await Client.GetAsync($"/api/stock/filings/{AppleId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var filings = await response.Content.ReadFromJsonAsync<List<FilingRow>>();
        var tenK = Assert.Single(filings!, filing =>
            filing.Form == "10-K" && filing.PrimaryDocument == "aapl-20230930.htm");
        Assert.Equal(
            "https://www.sec.gov/Archives/edgar/data/320193/000032019323000106/aapl-20230930.htm",
            tenK.DocumentUrl);
    }

    [Fact]
    public async Task Filing_AppleDocument_ReturnsText()
    {
        var response = await Client.GetAsync(
            $"/api/stock/filing/{AppleId}?accession=0000320193-23-000106&doc=aapl-20230930.htm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Apple Inc.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Filing_MissingQueryParams_Returns400()
    {
        var response = await Client.GetAsync($"/api/stock/filing/{AppleId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record ResolveResult(string Ticker, string Cik);
    private record FilingRow(string Form, string? FilingDate, string? AccessionNumber,
        string? PrimaryDocument, string? DocumentUrl);
}
