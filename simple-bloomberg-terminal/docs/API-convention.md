# API Convention

How to build the JSON API layer for this project. Read this before adding or
changing anything under `Controllers/Api/`, `Dtos/`, or `Services/`.

## 1. Goal

Implement complete REST API support (CRUD + DTOs) for all entities where
business rules allow, plus one external data integration (official stock data
from SEC EDGAR).

This is a **2-point assignment requirement**:
- CRUD for every entity (GET all + search, GET by id, POST, PUT, DELETE).
- API must not expose unnecessary internal entity fields → use DTOs.
- Related data shown through nested DTOs where it makes sense.

## 2. Core decision: the API is ADDITIVE

The API layer is an **addition**, not a replacement. Nothing in the current
MVC pipeline changes.

```
Browser (HTML forms)           JSON client / external caller
       │                                │
       ▼                                ▼
 MVC Controllers                  API Controllers   ◄── NEW
 returns View(vm)                 returns Ok(dto)
 uses ViewModels                  uses DTOs
       │                                │
       └────────────┬───────────────────┘
                     ▼
               Repositories          ◄── SHARED, untouched
                     │
                     ▼
                  EF Core
```

Rules:
- Do **not** modify existing MVC controllers, Razor views, or ViewModels.
- API controllers call the **same repositories** the MVC controllers call.
- `TickerController` (`/api/ticker`) is the existing precedent — generalize it.

## 3. When to add a service layer (and when NOT to)

Default: **no service layer**. Repos are already the injectable data-access
boundary. Adding a service that just forwards one call to one repo is an empty
pass-through — forbidden (violates project CLAUDE.md "no abstraction without a
demonstrated problem").

- **Plain CRUD + DTO** → controller maps entity↔DTO and calls the repo
  directly. No service.
- **SEC filing access** → `IStockApiClient` is the HTTP boundary. Controllers
  expose ticker resolution, filing metadata, and primary filing documents.

## 4. Folder layout

```
Controllers/Api/        ← one API controller per entity, [ApiController]
Dtos/                   ← request + response DTOs (nested where related)
Services/Clients/Edgar/ ← IStockApiClient + StockApiClient
MappingProfile.cs       ← AutoMapper profile (project root or Dtos/)
```

## 5. API controller conventions

- Attribute: `[ApiController]` + `[Route("api/[controller]")]` (e.g. `/api/companies`).
- Inject the existing repository (and `IMapper`). No `AppDbContext` in controllers.
- One controller per entity. CRUD surface:

| Verb + route                 | Repo call                  | Returns                       |
|------------------------------|----------------------------|-------------------------------|
| `GET  /api/companies`        | `GetAll()` / `Search(q)`   | `Ok(List<XxxDto>)`            |
| `GET  /api/companies/{id}`   | `GetById(id)`              | `Ok(XxxDto)` or `NotFound()`  |
| `POST /api/companies`        | `Add(entity)`             | `CreatedAtAction(..., dto)`   |
| `PUT  /api/companies/{id}`   | `Update(entity)`          | `Ok(dto)` or `NotFound()`     |
| `DELETE /api/companies/{id}` | `SoftDelete(id)`          | `NoContent()`                 |

- Search: when the repo has a `Search`/`Lookup` method, `GET all` accepts a
  `?q=` query param and routes to it; otherwise plain `GetAll()`.
- `[ApiController]` gives automatic model validation → invalid request DTOs
  return `400` with no extra code.

## 6. DELETE = soft delete + business rules

- `DELETE` maps to the existing `SoftDelete(id)` on each repo (sets `DeletedAt`).
  Do **not** hard-delete.
- "Where business rules allow": some deletes throw by design — e.g.
  `CompanyRepository.SoftDelete` throws `InvalidOperationException` when active
  revenue/cost sources exist. Catch these in the controller and return
  `409 Conflict` with the message. Entities whose rules forbid deletion expose
  no DELETE endpoint.

## 7. DTO conventions

- **Response DTOs** omit internal fields: never expose `DeletedAt`, raw audit
  fields, or bare FK ids when a nested object is more useful.
- **Request DTOs** (create/update) accept only client-settable fields. Server
  sets `Id`, timestamps, `DeletedAt`.
- **Nested DTOs** for related data where it makes sense:
  `CompanyDto { ..., CountryDto? Country, List<RevenueSourceDto> RevenueSources }`.
  Keep nesting shallow — one level deep unless a screen needs more. Avoid
  cycles (a related-company DTO should not re-nest its own relations).
- Use `record` types (matches existing `TickerItem`, `GraphNode` style).

## 8. Mapping — AutoMapper

Chosen approach: **AutoMapper** (convention-based, less per-entity boilerplate).

```csharp
// Program.cs
builder.Services.AddAutoMapper(typeof(Program));

// MappingProfile.cs
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Company, CompanyDto>();      // nested maps resolve automatically
        CreateMap<CompanyCreateDto, Company>();
        // ... one pair per entity/DTO
    }
}

// controller
var dto = _mapper.Map<CompanyDto>(company);
```

- One `CreateMap` per direction needed. Nested DTOs map automatically when their
  own maps are registered.
- For updates, map the request DTO onto the existing tracked entity:
  `_mapper.Map(updateDto, entity)` then `repo.Update(entity)`.

## 9. External stock data — SEC EDGAR filing access

Source: **SEC EDGAR** (free, no key, official US filings). `Company.Cik` is the
key (10-digit zero-padded). Endpoints in memory `reference_sec_edgar_api.md`.

### SEC filing access

`IStockApiClient` is an HTTP-only boundary for ordinary EDGAR filing access:

- `GetSubmissions(cik10)` lists filings.
- `GetFilingDocument(cik, accession, document)` downloads a primary filing document.
- `ResolveCik(ticker)`, `GetCikTickerMap()`, and `GetTickerEntries()` use SEC ticker metadata.

The application does not fetch structured financial-fact feeds or persist automatically derived financial
rows. Revenue, cost, and risk records come from filing-text extraction, manual entry, or other explicit
sources. SEC requests retain the required identifying `User-Agent` and respect SEC rate limits.

### Endpoints

- `GET /api/stock/resolve/{ticker}` — ticker-to-CIK lookup.
- `GET /api/stock/filings/{companyId}` — recent filing metadata.
- `GET /api/stock/filing/{companyId}?accession=&doc=` — proxied primary filing document.

## 10. DI registration (Program.cs)

Match the existing `AddScoped<IXxxRepository, XxxRepository>()` style:
- AutoMapper: `builder.Services.AddAutoMapper(typeof(Program));`
- Stock client: `builder.Services.AddHttpClient<IStockApiClient, StockApiClient>();`
- No new DI registration needed for plain CRUD controllers (they reuse repos).

## 11. Entities to cover

All ten current repos (skip a verb only where business rules forbid it):
Country, Company, Event, CountryDetails, TradeBloc, CountryAdvantage,
CountryChallenge, GdpSnapshot, RevenueSource, CostSource.

## 12. Per-entity checklist

For each entity:
1. Response DTO + create/update request DTO(s) in `Dtos/` (nested where useful).
2. `CreateMap` entries in `MappingProfile`.
3. `Controllers/Api/XxxController.cs` with `[ApiController]`, the 5 CRUD actions,
   search on GET-all if the repo supports it.
4. Map `DELETE` to `SoftDelete`; return `409` on business-rule exceptions.
5. Reuse the existing repo — do not add a service unless logic beyond CRUD appears.

## 13. Do NOT

- Do not add a service that only forwards to a repo.
- Do not touch existing MVC controllers, views, or ViewModels.
- Do not inject `AppDbContext` into controllers (use repos; add a repo method if
  a query doesn't fit — see `GetWithGraphRelations` precedent).
- Do not return entities directly or expose `DeletedAt`/internal fields.
