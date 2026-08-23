# Measuring the COST extraction pipeline

Reference for writing this up. Covers what is measured, how, and what had to change in the pipeline
to make it measurable.

## The pipeline under test

Two LLM layers, graded separately:

| Layer | Model tier | Job |
|---|---|---|
| **Fast workers** | DeepSeek fast | Read one chunk of the filing, return counterparties + verbatim evidence as JSON |
| **Lead agent** | DeepSeek pro | Read the merged worker findings + tagged XBRL, return the consolidated ledger |

One filing is split into **chunks** (~34 for a 10-K). A chunk is one worker call, covering either
several packed sub-headings of an SEC Item or one rendered financial statement.

## The three metrics

**Repeatability.** The same extraction of the same filing runs N times. Each claim is keyed by
`(direction, normalised counterparty name)`; normalisation lowercases, strips punctuation and drops
corporate suffixes, so "Ablecom Technology Inc." and "Ablecom Technology" are one company rather than
two. A key is stable when it appears in *every* run. Value stability is tracked separately. Variation
in the free-text `what` field is counted (`whatVariants`) but never scored as instability. Automatic.

**Groundedness.** Each claim's `evidence` string is checked against the filing text the workers
actually read. Whitespace and punctuation are normalised to lowercase alphanumeric tokens; a literal
match passes outright, otherwise token overlap over a same-length window must reach **0.90**. Scored
independently for each layer. Automatic.

**Precision.** One run of one layer is annotated by hand in the review grid, into
`ISPRAVNA` / `POGRESNO_KLASIFICIRANA` / `POGRESNA_VRIJEDNOST` / `NIJE_COUNTERPARTY`. Precision is the
share of judged claims marked correct. The fourth bucket exists because a competitor or litigation
adversary returned as a commercial counterparty is invisible to groundedness — the quote is real and
the company is named. Manual.

**Retention** (supporting figure). Mean lead items ÷ mean worker items. Recall is the hole in the
metric set — groundedness and precision both only see what *was* reported, never what was silently
dropped — and this is the cheapest available proxy.

## Harness design

Every run is a **full pipeline run**: its own worker scan, then its own lead call. Run 1 executes
alone as a cache warm-up (a cold filing means fetching the 10-K plus up to twenty rendered statement
files from SEC; N runs racing into those misses would be throttled, and SEC throttling *degrades* a
scan silently rather than failing it). The remaining runs execute concurrently, up to 10 at a time.

Three things make concurrency safe:

1. **Per-run DI scope.** `ICompanyRepository` is scoped and both services use it, so a shared scope
   meant concurrent operations on one `DbContext`. Each run resolves its own services.
2. **Explicit grounding.** Each run's lead call is handed *its own* scan's digest rather than
   resolving one, because the `filing-findings` cache key is per-filing — a resolving run would be
   graded against whichever concurrent scan finished last.
3. **Shared deterministic caches.** The raw filing, parsed headings and rendered reports are pure
   fetch/parse results, reused across runs on purpose: sharing them removes no model variance.

**Worker claims are captured pre-merge**, from the raw per-chunk replies. Post-merge output would
fold in scheduler nondeterminism, since which duplicate survives depends on task completion order.

**Worker errors are counted per run.** A failed worker call is swallowed and returns an empty list —
correct in production, but indistinguishable from a chunk that honestly contained nothing, so it
deflates yield with no signal. A non-zero count invalidates the yield and retention figures.

Concurrency ceiling: 10 runs × 6-wide workers = 60 simultaneous fast calls, against DeepSeek's
2,500 concurrent-connection limit on the fast tier and 500 on the strong tier.

## Pipeline changes made to enable measurement

**1. COST extraction became counterparty-based.** It previously asked for
`classification ∈ {COGS, OPEX, TOTAL_COSTS}` — an accounting judgement the filing never states, so
nothing in the text can back it and groundedness is undefined for that field. The unit is now a named
counterparty: `counterparty`, `direction ∈ {SUPPLIER, CUSTOMER, PARTNER}`, `what`, `value`,
`evidence`. Every field is a fact printed on the page, so every field is checkable. The accounting
bucket is now derived at save time (`SUPPLIER → COGS`), which is what the existing save paths already
defaulted to.

**2. LLM heading triage removed.** A model call used to read the heading titles and choose which
sections to scan, so two runs of the "same" extraction were not reading the same text. Section
selection is now deterministic: every detected heading is scanned, with keyword-relevance ranking
trimming to a fixed 48-chunk budget. Chunks/run is now constant across runs, which isolates the
measured variance to what the models do with identical input.

**3. A second output mode on the lead agent.** Ordinary chat replies are free-form prose — useful for
a human reviewer, unmeasurable across runs. A fixed prompt now triggers a structured `ledger` block
with the fixed column set. Conversational behaviour is unchanged.

**4. Groundedness corpus corrected.** Item 8 does not come from the filing document — it comes from
SEC's separately fetched rendered report files. Checking evidence only against the document scored
every financial-statement quote as ungrounded despite being verbatim.

## Other changes

- **Measurement page** at `/extraction/measure`, reachable from a company's COST SOURCES section.
  Runs as a detached background job with a live per-run tracker (chunk tree, then lead result).
- **Review grid** for the manual precision pass: filter by layer and run, expand any claim's evidence,
  jump to the passage in the filing on EDGAR via a text-fragment link, pick a judgement. Live
  precision readout over judged claims only.
- **One CSV**, one row per (filing, run, layer, claim), with run- and filing-level figures
  denormalised onto each row and a blank `judgement` column. Generated in the browser so annotations
  are included.

## Caveats for the write-up

- The `mean` row's Ground % is **pooled across all claims**, not the mean of per-run percentages.
  These differ when runs produce different item counts. Pooled is the figure to report.
- Repeatability at low N is weak evidence — N=2 is the most lenient possible test.
- Temperature is not set; the provider default applies and thinking is always enabled. Record the
  model id, which the harness captures per run.
- The safe concurrency ceiling is a property of the routed provider, not of the harness.
