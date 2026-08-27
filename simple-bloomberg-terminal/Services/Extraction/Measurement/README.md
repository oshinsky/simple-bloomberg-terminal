# Counterparty extraction measurement

This folder contains the measurement orchestration, versioned experimental treatment, and pure scoring
layer for the filing counterparty pipeline.

## Runtime flow

```text
Measure.cshtml
    → ExtractionController.cs
    → CounterpartyMeasurementService.cs
    → FastWorkerScanService.cs
    → parallel fast worker agents
    → FilingAnalysisContextService.cs
    → LeadAgentRunner.cs (lead agent)
    → MeasurementCalculator.cs
    → Measure.cshtml
```

1. `ExtractionController.cs` starts a background job and `MeasureJobStore.cs` holds its progress.
2. `CounterpartyMeasurementService.cs` fixes the node to COST and repeats the complete pipeline. Run 1
   warms deterministic caches; the remaining runs execute concurrently.
3. `FastWorkerScanService.cs` fetches the primary filing through `StockApiClient.cs`.
4. `FilingSections.cs` flattens HTML without interpreting tables, detects SEC Items and headings,
   preserves relevant text, and deterministically ranks bounded chunks.
5. `FastWorkerScanService.cs` sends chunks to six parallel fast worker agents using the shared production
   COST direction in `CounterpartyPrompts.cs`. The scan returns typed worker claims and reduces them into
   one fast-worker digest.
6. `FilingAnalysisContextService.cs` supplies that run's filing digest, and
   `LeadAgentRunner` executes the fixed measurement contract from `MeasurementPrompts.cs`. The lead
   returns an `items` ledger. Measurement does not pass through the conversational chat service.
7. `CounterpartyMeasurementService.cs` creates one `CounterpartyRunResult` per repetition and passes all
   runs to `MeasurementCalculator.cs`. The calculator performs evidence, repeatability, and retention
   scoring; the controller returns the result to `Measure.cshtml`.

The fast worker agents do not message the lead agent directly. Their JSON is parsed and reduced into a
fast-worker digest, which the host injects into the lead-agent call. This is a hierarchical map/reduce
multi-agent architecture.

`[SHARED]` files belong to both conversational chat extraction and measurement extraction.
`[MEASUREMENT]` files exist only to repeat, standardize, or score the experiment.

```text
CounterpartyMeasurementService.cs           [MEASUREMENT]
    ├─ FastWorkerScanService.cs              [SHARED]
    │   ├─ StockApiClient.cs                 [SHARED]
    │   ├─ FilingSections.cs                 [SHARED]
    │   └─ CounterpartyPrompts.cs            [SHARED: COST/REVENUE fast-worker prompt]
    │
    ├─ MeasurementSupport.cs                 [MEASUREMENT]
    ├─ FilingAnalysisContextService.cs       [SHARED CONTEXT]
    ├─ LeadAgentRunner.cs                    [SHARED EXECUTION]
    ├─ MeasurementPrompts.cs                 [MEASUREMENT: lead-agent ledger contract]
    │
    ├─ CounterpartyModels.cs                 [MEASUREMENT]
    └─ MeasurementCalculator.cs              [MEASUREMENT]
```

The normal chat and measurement paths are sibling orchestrators. Both use the shared scan, filing-context,
and lead-agent execution services. `ExtractionChatService` adds conversation and save blocks;
`CounterpartyMeasurementService` instead adds repeated runs, a fixed ledger contract, artifact capture,
and scoring.

## File responsibilities

| File | Responsibility |
|---|---|
| `Views/Extraction/Measure.cshtml` | Starts a measurement and presents progress and results. |
| `Controllers/ExtractionController.cs` | Validates targets and manages the background HTTP workflow. |
| `Services/Extraction/Measurement/MeasureJobStore.cs` | Stores temporary job progress and completed results. |
| `Services/Extraction/Measurement/CounterpartyMeasurementService.cs` | Repeats and coordinates full COST pipeline runs. |
| `Services/Extraction/FastWorkerScanService.cs` | Fetches inputs, plans chunks, runs fast worker agents, and builds their digest. |
| `Services/Extraction/FilingSections.cs` | Parses and filters filing sections and plans deterministic chunks. |
| `Services/Clients/Edgar/StockApiClient.cs` | Downloads filing documents from EDGAR. |
| `Services/Extraction/FilingAnalysisContextService.cs` | Builds shared filing-digest and raw-text fallback context. |
| `Services/Extraction/LeadAgentRunner.cs` | Executes prompt-agnostic lead-agent calls. |
| `Services/Extraction/Chat/ExtractionChatService.cs` | Adds interactive conversation and save-block prompts; measurement does not depend on it. |
| `Services/Extraction/CounterpartyPrompts.cs` | Owns the directional COST/REVENUE fast-worker contract. |
| `MeasurementPrompts.cs` | Owns the versioned measurement lead-agent contract. |
| `CounterpartyModels.cs` | Defines run artifacts and measurement output records. |
| `MeasurementSupport.cs` | Parses lead-agent ledger JSON, normalizes counterparty identities, and checks evidence against the worker corpus. |
| `MeasurementCalculator.cs` | Calculates all metrics without network, UI, or model access. |

## Measurement rules

- Evidence is searched only in the exact `ExtractionChunkArtifact` corpus observed by workers.
- Repeatability identity is `(direction, normalized counterparty)`.
- Corporate suffixes and formatting do not create separate identities.
- Fast-worker and lead-agent layers are scored separately.
- Precision remains a manual annotation step because the filing corpus is not a complete gold label.

Both `CounterpartyPrompts.Version` and `MeasurementPrompts.Version` are recorded with every experiment.
Changing either prompt or output schema creates a new treatment and should increment its version.

## Counterparty name variation

The same company can be extracted under different names, such as `NVIDIA`, `NVIDIA Corp.`, or
`NVIDIA Corporation`. Treating these as separate counterparties creates duplicate rows and makes the
extraction appear less repeatable than it actually is.

Before scoring, the measurement layer normalizes case, punctuation, whitespace, and common legal suffixes.
The raw name is retained for auditing. Ambiguous aliases such as `AMD` and `Advanced Micro Devices`
are not merged automatically because an incorrect match could artificially improve the result. A
company's normalized identity and its relationship direction are measured separately.
