using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Extraction flow: create a revenue row, then freeze proof onto that row.
/// Proof is one pair per row — Reference (where in the document) + Evidence (the verbatim passage) —
/// stored on the row itself along with the filing it came from.
/// </summary>
public class ExtractionTests : ApiTestBase
{
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;

    private record RefResult(long SourceId);

    private async Task<HttpResponseMessage> PostExtractionAsync(string url, object body)
    {
        var html = await Client.GetStringAsync("/extraction");
        var match = Regex.Match(html,
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Extraction page did not render an antiforgery token.");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("RequestVerificationToken", WebUtility.HtmlDecode(match.Groups[1].Value));
        return await Client.SendAsync(request);
    }

    private Task<long> RefreshAppleAndGetRevenueRowId()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = new RevenueSource($"Revenue test {Guid.NewGuid():N}", AppleId)
            { DataSource = DataSource.MANUAL };
        db.RevenueSources.Add(row);
        db.SaveChanges();
        return Task.FromResult(row.Id);
    }

    [Fact]
    public async Task Reference_OnEdgarRevenueRow_WritesProofOntoTheRow()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();

        var resp = await PostExtractionAsync("/extraction/reference", new
        {
            companyId = AppleId,
            sourceId = rowId,
            node = "REVENUE",
            name = "Revenue 2023",
            value = 383_000_000_000d,
            reference = "Item 7. Management's Discussion",
            evidence = "\"val\": 383000000000"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<RefResult>();
        Assert.Equal(rowId, result!.SourceId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.RevenueSources.Single(r => r.Id == rowId);

        Assert.Equal("Item 7. Management's Discussion", row.Reference);
        Assert.Equal("\"val\": 383000000000", row.Evidence);
        Assert.Null(row.FilingId);   // no filing metadata was supplied
    }

    [Fact]
    public async Task Reference_SameRowTwice_OverwritesTheProofInPlace()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();

        async Task<RefResult> Ref(string evidence) =>
            (await (await PostExtractionAsync("/extraction/reference", new
            {
                companyId = AppleId,
                sourceId = rowId,
                node = "REVENUE",
                name = "Revenue 2023",
                reference = "Item 8. Financial Statements",
                evidence
            })).Content.ReadFromJsonAsync<RefResult>())!;

        var first = await Ref("first proof");
        var second = await Ref("second proof");

        Assert.Equal(first.SourceId, second.SourceId);   // same row reused

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.RevenueSources.Single(r => r.Id == rowId);
        Assert.Equal("second proof", row.Evidence);
    }

    [Fact]
    public async Task Reference_NoSourceRow_CreatesTheRowWithItsProof()
    {
        var resp = await PostExtractionAsync("/extraction/reference", new
        {
            companyId = AppleId,
            sourceId = (long?)null,   // user-created row
            node = "REVENUE",
            name = "Services",
            value = 85_000_000_000d,
            reference = "Item 8, Segment note",
            evidence = "services revenue 85B"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<RefResult>();
        Assert.True(result!.SourceId > 0);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.RevenueSources.Single(r => r.Id == result.SourceId);
        Assert.Equal("Services", row.Name);
        Assert.Equal(DataSource.MANUAL, row.DataSource);
        Assert.Equal("services revenue 85B", row.Evidence);
    }

    [Fact]
    public async Task References_AfterReferencing_ReturnsTheRowsProof()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();
        await PostExtractionAsync("/extraction/reference", new
        {
            companyId = AppleId,
            sourceId = rowId,
            node = "REVENUE",
            name = "Revenue 2023",
            reference = "Item 7A. Quantitative Disclosures",
            evidence = "val 383000000000"
        });

        var proof = await Client.GetFromJsonAsync<RefRow>($"/extraction/references/{rowId}?node=REVENUE");
        Assert.Equal("Item 7A. Quantitative Disclosures", proof!.Reference);
        Assert.Equal("val 383000000000", proof.Evidence);
        Assert.Null(proof.Filing);
    }

    private record RefRow(string? Reference, string? Evidence, string? Filing);

    [Fact]
    public async Task Reference_MissingEvidence_Returns400()
    {
        var resp = await PostExtractionAsync("/extraction/reference", new
        {
            companyId = AppleId,
            sourceId = (long?)null,
            node = "REVENUE",
            name = "X",
            evidence = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Save_MissingNode_Returns400()
    {
        var resp = await PostExtractionAsync("/extraction/save", new
        {
            companyId = AppleId,
            name = "Missing node"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Invalid extraction node", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Save_RowFromAnotherCompany_Returns400WithoutChangingIt()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();
        var resp = await PostExtractionAsync("/extraction/save", new
        {
            companyId = CustomWebApplicationFactory.CompanyBlockedId,
            sourceId = rowId,
            node = "REVENUE",
            name = "Should not be written"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotEqual("Should not be written", db.RevenueSources.Single(row => row.Id == rowId).Name);
    }

    [Fact]
    public void NonReviewerEdit_PreservesExistingProofOnPendingReplacement()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = new RevenueSource("Existing", AppleId)
        {
            Reference = "Item 7",
            Evidence = "Original evidence",
            DataSource = DataSource.MANUAL,
            Status = ContributionStatus.Approved
        };
        db.RevenueSources.Add(existing);
        db.SaveChanges();

        var writer = scope.ServiceProvider.GetRequiredService<IContributionWriter>();
        var proposalId = writer.UpsertRow(
            ExtractionNode.REVENUE, AppleId, existing.Id, null, "Edited", null, null, null, null,
            new Contributor(false, null));

        var proposal = db.RevenueSources.Single(row => row.Id == proposalId);
        Assert.Equal(ContributionStatus.Pending, proposal.Status);
        Assert.Equal(existing.Id, proposal.SupersedesId);
        Assert.Equal("Item 7", proposal.Reference);
        Assert.Equal("Original evidence", proposal.Evidence);
    }

    [Fact]
    public async Task Reference_AfterFilingSoftDeleted_RevivesInsteadOfDuplicateInsert()
    {
        const string accession = "0000000000-99-000777";
        var rowId = await RefreshAppleAndGetRevenueRowId();

        Task<HttpResponseMessage> RefWithFiling() => PostExtractionAsync("/extraction/reference", new
        {
            companyId = AppleId, sourceId = rowId, node = "REVENUE", name = "Revenue 2023",
            reference = "Item 8", evidence = "snap",
            filingAccessionNumber = accession, filingForm = "10-K", filingDate = "2023-11-03", filingUrl = "http://x"
        });

        // 1. First reference creates the Filing and links the row to it.
        Assert.Equal(HttpStatusCode.OK, (await RefWithFiling()).StatusCode);

        long filingId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var f = db.Filings.Single(x => x.AccessionNumber == accession);
            filingId = f.Id;
            Assert.Equal(filingId, db.RevenueSources.Single(r => r.Id == rowId).FilingId);
            f.DeletedAt = DateTime.UtcNow;   // simulate a prior source-cluster delete
            db.SaveChanges();
        }

        // 2. Re-referencing the same accession must revive the row, not insert a duplicate
        //    (which would hit the unique accession index).
        Assert.Equal(HttpStatusCode.OK, (await RefWithFiling()).StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var filings = db.Filings.Where(x => x.AccessionNumber == accession).ToList();
            Assert.Single(filings);                  // not duplicated
            Assert.Equal(filingId, filings[0].Id);   // same row revived
            Assert.Null(filings[0].DeletedAt);       // brought back to life
        }
    }
}
