# Extraction implementation plan

This document records the current direction for SEC extraction.

## Current architecture

- `IStockApiClient` resolves ticker/CIK metadata, lists filings, and downloads primary filing documents.
- Extraction operates on normalized filing text and filing sections.
- Revenue and cost extraction identify named counterparties and cite filing evidence.
- Risk extraction identifies material risks and cites filing evidence.
- Chat and measurement remain the two consumers of the shared extraction context.
- Contributions are persisted through `Services/Contributions/ContributionWriter.cs`, whose
  `IContributionWriter` contract preserves the reviewer gate and citations.

## Explicitly excluded

- Parsing filing tables into financial rows.
- Automatically creating aggregate revenue or cost records from structured feeds.
- Treating an aggregate figure as evidence of a named counterparty relationship.

## Verification

Run the application and test projects after changes:

```powershell
dotnet build simple-bloomberg-terminal/simple-bloomberg-terminal.csproj --no-restore
dotnet test simple-bloomberg-terminal.Tests/simple-bloomberg-terminal.Tests.csproj --no-restore
```
