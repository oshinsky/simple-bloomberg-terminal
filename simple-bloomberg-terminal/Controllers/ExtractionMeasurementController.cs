using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("extraction")]
[Authorize]
public sealed class ExtractionMeasurementController(
    MeasureJobStore jobs,
    IServiceScopeFactory scopeFactory,
    IUserApiKeyProvider keys) : Controller
{
    [HttpGet, Route("measure")]
    public IActionResult Measure(long? companyId, string? accession, string? doc, string? form)
    {
        var vm = new MeasureViewModel();
        if (companyId is { } id && !string.IsNullOrWhiteSpace(accession) && !string.IsNullOrWhiteSpace(doc))
            vm.Targets = string.Join(", ", new[] { id.ToString(), accession, doc, form }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        return View("~/Views/Extraction/Measure.cshtml", vm);
    }

    [HttpPost, Route("measure/start"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Start([FromBody] MeasureViewModel vm)
    {
        var userKeys = await keys.GetAsync();
        RequireParsingKey(userKeys);

        var targets = ParseTargets(vm.Targets);
        if (targets.Count == 0) return BadRequest("No valid target lines.");

        var runs = Math.Clamp(vm.Runs, 2, 20);
        var job = new MeasureJob { Runs = runs, StrictCounterparties = vm.StrictCounterparties };
        jobs.Add(job);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<IUserApiKeyProvider>().Set(userKeys);
            var measurement = services.GetRequiredService<CounterpartyMeasurementService>();
            var companies = services.GetRequiredService<ICompanyRepository>();

            try
            {
                foreach (var target in targets)
                {
                    var label = companies.GetById(target.CompanyId)?.Name ?? $"#{target.CompanyId}";
                    try
                    {
                        var result = await measurement.MeasureAsync(
                            target.CompanyId, target.Accession, target.Doc, runs, target.Form,
                            job.StrictCounterparties, progress => job.Apply(progress with { Filing = label }));
                        lock (job.Lock) job.Results.Add(result);
                    }
                    catch (Exception ex) when (
                        ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                    {
                        lock (job.Lock)
                            job.Results.Add(new CounterpartyMeasurementResult(
                                label, "", target.Accession, runs,
                                0, "", DateTime.UtcNow, [], ex.Message));
                    }
                }

                lock (job.Lock)
                {
                    job.RowsJson = JsonSerializer.Serialize(
                        job.Results.SelectMany(result => result.Rows),
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    job.Status = MeasureJobStatus.Done;
                }
            }
            catch (Exception ex)
            {
                lock (job.Lock)
                {
                    job.Error = ex.Message;
                    job.Status = MeasureJobStatus.Error;
                }
            }
        });

        return Json(new { jobId = job.Id });
    }

    [HttpGet, Route("measure/status/{jobId}")]
    public IActionResult Status(string jobId)
    {
        var job = jobs.Get(jobId);
        if (job is null) return NotFound();
        lock (job.Lock)
            return Json(new
            {
                status = job.Status.ToString(),
                error = job.Error,
                runs = job.RunStates
                    .OrderBy(run => run.Filing).ThenBy(run => run.Run)
                    .Select(run => new
                    {
                        run.Filing, run.Run, run.Phase, run.FastWorkerClaims, run.Errors, run.LeadAgentClaims,
                        chunksTotal = run.Chunks.Count,
                        chunksDone = run.Chunks.Count(chunk => chunk.Status is "Done" or "Error"),
                        chunks = run.Chunks.Select(chunk => new
                        {
                            chunk.Titles, chunk.Status, chunk.Found,
                            error = chunk.Status == "Error" ? chunk.Response : null,
                        }),
                    }),
            });
    }

    [HttpGet, Route("measure/result/{jobId}")]
    public IActionResult Result(string jobId)
    {
        var job = jobs.Get(jobId);
        if (job is null) return NotFound();
        lock (job.Lock)
            return View("~/Views/Extraction/Measure.cshtml", new MeasureViewModel
            {
                Runs = job.Runs,
                StrictCounterparties = job.StrictCounterparties,
                Results = job.Results.ToList(),
                RowsJson = job.RowsJson,
            });
    }

    private static List<(long CompanyId, string Accession, string Doc, string? Form)> ParseTargets(string? input)
    {
        var targets = new List<(long, string, string, string?)>();
        foreach (var line in (input ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length < 3 || !long.TryParse(fields[0], out var companyId)) continue;
            targets.Add((companyId, fields[1], fields[2],
                fields.Length > 3 && fields[3].Length > 0 ? fields[3] : null));
        }
        return targets;
    }

    private static void RequireParsingKey(UserApiKeys userKeys)
    {
        if (string.IsNullOrWhiteSpace(userKeys.KeyFor(userKeys.ParsingProvider)))
            throw MissingApiKeyException.ForParsingProvider(userKeys.ParsingProvider);
    }
}
