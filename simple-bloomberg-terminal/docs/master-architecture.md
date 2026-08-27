# Master Architecture

Current-state map of the SEC filing extraction subsystem. The application is ASP.NET Core MVC with
EF Core repositories, EDGAR clients, routed LLM providers, and in-memory stores for detached jobs.

## System boundary

The extraction UI operates on one company filing and one `ExtractionNode`:

- `REVENUE` produces named revenue-side counterparty candidates.
- `COST` produces named cost-side counterparty candidates.
- `RISK` produces disclosed risk candidates.

Counterparty web discovery is a separate enrichment path. It does not replace filing extraction.

## Extraction architecture

```text
Primary SEC filing document
                         |
                         v
                 FastWorkerScanService
                 - selects SEC Items
                 - flattens HTML and detects headings
                 - ranks and chunks text
                 - runs bounded parallel workers
                 - merges findings into a digest
                         |
                         v
              FilingAnalysisContextService
                 - accepts the worker digest
                 - uses raw filing fallback if empty
                         |
                         v
                    LeadAgentRunner
                 - executes a supplied prompt
                 - owns no chat or measurement rules
                    /                 \
                   v                   v
       ExtractionChatService   CounterpartyMeasurementService
       conversation and saves  repeated COST runs, artifacts,
                               structured ledger, and scoring
```

Chat and measurement are sibling consumers. Measurement does not call `ExtractionChatService`.

## Shared core

| File | Responsibility |
|---|---|
| `Services/Extraction/FastWorkerScanService.cs` | Fetches filing inputs, creates the deterministic scan plan, runs parallel fast-model calls, and builds the findings digest. |
| `Services/Extraction/FilingSections.cs` | Selects SEC Items, cleans filing HTML, detects headings, ranks content, and enforces chunk budgets. |
| `Services/Extraction/FilingAnalysisContextService.cs` | Builds shared filing-digest and raw-text fallback context. |
| `Services/Extraction/LeadAgentRunner.cs` | Executes prompt-agnostic lead-agent calls. |
| `Services/Clients/Edgar/StockApiClient.cs` | Downloads primary filing documents, submissions, and ticker metadata. |

Filing prose supplies names, relationships, risks, and verbatim evidence. Counterparty values remain
null unless the filing explicitly attributes a figure to the named company.

## Interactive extraction and chat

`ExtractionChatService` adds the production conversational contract to the shared core:

- conversation history and streaming replies;
- node-specific review and `save` block schemas;
- cached-digest lookup and scan-on-first-chat behavior.

The normal detached flow is:

```text
Views/Extraction/Index.cshtml
  -> POST /extraction/scan-auto-async/{companyId}
  -> ScanJobStore
  -> FastWorkerScanService
  -> FilingAnalysisContextService + LeadAgentRunner through ExtractionChatService
  -> wwwroot/js/site.js polls /extraction/scan-jobs
```

`ScanJobStore` holds transient progress, per-chunk inspection data, the initial summary, and follow-up
reply buffers. It is a singleton because work continues after the starting HTTP request ends.

## Measurement

`CounterpartyMeasurementService` is a wrapper around the shared extraction core. It:

1. fixes the node to `COST`;
2. runs the complete fast-worker and lead-agent cycle N times;
3. warms deterministic EDGAR/parsing caches with run 1;
4. runs remaining repetitions concurrently in independent DI scopes;
5. passes each run's own digest explicitly into filing-context construction;
6. parses worker and lead outputs into the same claim model;
7. calculates repeatability, evidence presence, and retention.

The versioned measurement prompt and pure scoring code live in `Services/Extraction/Measurement`.
The measurement contract is independent of conversational save prompts.
Unlike interactive chat, the measurement lead step uses one non-streaming completion capped at 16,000
output tokens. It retries once when the provider times out or ends the HTTP response prematurely, logs
the provider/model and digest size, and records a lead error without discarding the rest of the batch.

```text
Views/Extraction/Measure.cshtml
  -> POST /extraction/measure/start
  -> MeasureJobStore
  -> CounterpartyMeasurementService
  -> shared scan + context + lead-agent services
  -> MeasurementCalculator
  -> GET /extraction/measure/result/{jobId}
```

## Persistence and enrichment

Extraction itself proposes records; it does not persist them automatically. Confirmed `save` blocks
are sent through `ExtractionController` to `ContributionWriter`, which writes `RevenueSource`,
`CostSource`, or `CompanyRisk` records with filing provenance and evidence.

Named counterparties can be resolved or provisioned through the discovery/provisioning services and
linked with reciprocal revenue/cost relationships. This happens after extraction and remains separate
from measurement, which is read-only.

## State and service lifetimes

- Scan, chat, context, measurement, and repositories are scoped services; typed HTTP clients are
  created by `IHttpClientFactory`.
- `ScanJobStore` and `MeasureJobStore` are singletons for detached-job polling.
- Every detached task creates its own DI scope; concurrent measurement repetitions also use separate
  scopes because repositories contain scoped EF Core `DbContext` instances.
- Raw filings, parsed headings, and worker digests are cached briefly in
  memory. Measurement shares deterministic fetch/parse caches but never shares run-specific model output.

Registrations are in `Program.cs`; HTTP orchestration is in `Controllers/ExtractionController.cs`.

## Invariants

- Worker evidence must be a verbatim substring of the text supplied to that worker.
- Section selection and chunk budgets are deterministic for repeatable measurement.
- A measurement lead call receives its own run's digest, never whichever shared cache entry finished last.
- Long-running work must not retain an HTTP request scope.
- Measurement remains read-only and independent of interactive-chat prompts.
- Saved records retain accession, filing reference, and evidence.

## Related documents

- `docs/measurement.md` — metrics, concurrency, and experimental caveats.
- `docs/ai-popup-chat.md` — detached scan/chat widget and client flow.
- `Services/Extraction/Measurement/README.md` — measurement folder and file map.
