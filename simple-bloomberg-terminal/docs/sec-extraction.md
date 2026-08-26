# SEC extraction

The extraction pipeline reads the primary SEC filing as plain text. It deliberately does not parse
financial tables or download the SEC's separately rendered `R*.htm` statement reports.

## Pipeline

```text
primary filing HTML
  -> FilingSections: plain text, SEC Item boundaries, headings, bounded chunks
  -> FastWorkerScanService: parallel node-specific workers
  -> filing digest
     -> ExtractionChatService
     -> CounterpartyMeasurementService
```

`FilingSections` removes scripts, styles, and markup, inserts ordinary whitespace at visual block and
cell boundaries, then identifies relevant SEC Items. It does not classify tables, retain rows or
columns, interpret units, or give table-shaped content a ranking bonus. Oversized paragraphs are split
without dropping their remaining text.

## Node contracts

### COST

Searches for named cost-side counterparties: suppliers, vendors, manufacturers, foundries, contract
producers, licensors, and service providers. A result requires verbatim filing evidence naming the
company and establishing the purchasing relationship.

### REVENUE

Searches for named revenue-side counterparties: customers, buyers, licensees, distributors, resellers,
and commercial revenue partners. It does not extract segments, products, regions, unnamed customer
concentrations, or revenue table rows. A relationship without a stated amount is valid.

### RISK

Searches Items 1A and 7A for clearly evidenced disclosed risks. It returns a short name, risk scope,
note, and verbatim evidence.

## Item routing

| Node | Items |
| --- | --- |
| COST | 1, 7, 8 |
| REVENUE | 1, 1A, 7, 8 |
| RISK | 1A, 7A |

Item 8 remains because narrative financial notes may name customers or suppliers. It is read only from
the primary filing and receives no table-specific treatment.

## Consumers

Chat and measurement keep separate lead-agent contracts while sharing the same filing chunks and fast
worker counterparty prompt. Measurement remains fixed to COST and can enable strict evidence mode.
