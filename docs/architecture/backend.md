# Backend architecture

Clean Architecture, CQRS and vertical slices on .NET 10.

```
Presenter (Angular)  ->  Ims.Api  ->  Ims.Application  ->  Ims.Domain
                                            ^
                          Ims.Infrastructure (EF Core, JWT, Excel, hashing) implements Application's interfaces
```

Dependencies point inward only. `Ims.Api` references `Ims.Infrastructure` solely to register services at start-up.
Architecture tests (`tests/Ims.Architecture.Tests`) fail the build if this rots.

## Layers

| Project | Holds | May reference |
|---|---|---|
| `Ims.Domain` | Data model (entities, enums, value objects), behaviour (entity methods + factories that guard their invariants), business services (posting engine and strategies, approval policy, document-action policy, costing, availability), business rules (`IBusinessRule`), constants | nothing |
| `Ims.Application` | Use cases as commands and queries with their handlers and validators, specifications, ports (interfaces), DTOs | Domain |
| `Ims.Infrastructure` | EF Core `DbContext` + migrations, `Repository<T>`, `UnitOfWork`, JWT, password hashing, Excel (ClosedXML), seed | Application, Domain |
| `Ims.Api` | Controllers, middleware, auth policies, OpenAPI/Swagger, logging, composition root | Application, Infrastructure (DI only) |

## Vertical slices

`Application/Features/<Feature>/` contains everything for that feature:

```
Features/Catalog/
  UseCases/CreateProduct/   CreateProductCommand.cs  CreateProductHandler.cs  CreateProductValidator.cs
  UseCases/ListProducts/    ListProductsQuery.cs     ListProductsHandler.cs
  Specifications/           ProductBySkuSpec.cs ...  (Ardalis.Specification: queries as named objects)
  Contracts/                ProductDto.cs ...         (request/response records)
  Services/                 IProductWriter.cs ProductWriter.cs
  Validators/               ProductSaveRequestValidator.cs   (shared DTO rules)
```

One public type per file. Controllers are grouped the same way under `Ims.Api/Features/<Feature>/`.

## CQRS and the pipeline

`IDispatcher.SendAsync(command | query)` is the only way the API enters the Application layer.
Pipeline (outermost first): `LoggingBehavior` -> `ValidationBehavior` (FluentValidation) -> handler.
Handlers depend on interfaces only (`IRepository<T>`, `IReadRepository<T>`, `IUnitOfWork`, `ISystemSettings`,
`IDocumentNumbering`, ...). Handlers never call each other; a handler that needs another use case sends a command.

## Domain-driven behaviour

Entities have private setters and factories (`Product.Create`, `StockDocument.Create`, ...). State changes happen through
methods (`Submit`, `Approve`, `Reject`, `MarkPosted`, `MarkReversed`, `Receive`, `Issue`) so an invalid state cannot be built.
Rules that span entities are domain services, not handler code: `PostingEngine` + `IPostingStrategy` per document type
(open/closed: a new document type adds a strategy), `ApprovalPolicy` (who needs to approve and which reviewer role a document is routed to),
`DocumentActionPolicy` (the `allowedActions` the UI shows AND the ownership guards the commands enforce, so a listed action is one the API accepts),
`StockAvailabilityPolicy`, `CostingPolicy`. They are pure and unit-tested without a database.

## Public ids

An entity a client addresses derives from `PublicEntity` (itself an `Entity`): besides the integer key it has a `PublicId`, a random GUID made when the object is created and never set from outside. The integer key stays the clustered key and the key of every foreign key; the public id is what leaves the system (decision and plan: [adr-guid-keys.md](adr-guid-keys.md)). Application code looks rows up with `ByPublicIdSpec` / `PublicIdsSpec` and translates lists with the scoped `IIdMap<T>` (one query per list). Until an entity's API flips (PR-4 to PR-7) nothing public carries the new id: audit row events keep naming rows by the integer key (the payload of a created or deleted row carries the `PublicId`), and flip with the entity's API. Storage details and the migration measurements are in [data-and-performance-rules.md](data-and-performance-rules.md) section 9.

## Persistence

`IRepository<T>` stages changes; `IUnitOfWork.SaveChangesAsync` commits them together with audit rows;
`IUnitOfWork.ExecuteAsync` wraps a use case in one transaction with retry. Reads use Ardalis specifications and projections.
`StockLedgerEntry` and `AuditLog` are append-only: refused by the `DbContext` and by database triggers.

## Concurrency and deadlocks

- Optimistic concurrency with `rowversion` on documents, balances and number sequences.
- One global lock order: document sequence, document, balances by ascending `ProductId`, ledger. Posting applies lines in `ProductId` order.
- `READ_COMMITTED_SNAPSHOT` is enabled by the first migration, so readers never block writers.
- If SQL Server still picks a victim (error 1205), hits a lock timeout (1222) or a unique key races, `ExecuteAsync` rolls back, discards tracked state and re-runs after a randomised back-off (max 5 attempts), then returns 409.
- No blocking async calls anywhere (`.Result`, `.Wait()`): enforced by an architecture test.

## Dependency injection

Each layer registers itself: `AddApplication()`, `AddInfrastructure(cfg)`, `AddPresentation(cfg)`. Handlers, helpers (`IAppService`)
and validators are found by convention. The container runs with `ValidateScopes` and `ValidateOnBuild`, so a mis-wired service fails
at start-up. Options (`JwtOptions`) are validated at start-up.

## API contract (what a generated client can rely on)

The OpenAPI document at `/openapi/v1.json` describes the wire format as it is, so a client generator needs no workaround:

- **Wire format** is defined once in `Ims.Api/Composition/WireJson.cs`: enums are strings, numbers are numbers, every instant is UTC with a trailing `Z`
  (`UtcDateTimeConverter` on the wire; `UtcKindConverter` in EF so a value is `Kind = Utc` everywhere in between).
- **Enums** are string schemas that list their values (`EnumSchemaTransformer` keeps `null` out of the shared list; nullability belongs to the property).
- **Operation ids** are `Tag_action` (controller name, action in camelCase), for example `Documents_createReceipt` (`OperationIdTransformer`). Renaming a C# action renames the client method; a test pins the ones the client uses.
- **Uploads** (`IFormFile`) are inline `string/binary` parts of a multipart body and required (`MultipartRequiredTransformer`).
- **Downloads** carry `[ProducesFile(contentType)]`; the document then declares a binary 200 body and the `Content-Disposition` header (`FileResponseTransformer`).
- **Documents** carry a concurrency token (`concurrencyToken`, also the `ETag` header). A write may send it back as `If-Match`; a stale token is refused with 409 before anything is changed (`DocumentVersion.EnsureCurrent`). Changing only the lines also moves the token (`AppDbContext.TouchDocumentsWithChangedLines`).
- A barcode lookup miss is `200 { found: false, product: null }`, not a 404 (a scan that matches nothing is a normal outcome).

## Cost and value visibility

Who may see cost and value is decided by two permissions, `cost.view` (Clerk, Supervisor, Owner, Admin) and `valuation.view` (Supervisor, Owner, Admin), and written once: a cost or value is tagged `[VisibleWith(permission)]` where it is declared (`Ims.Domain.VisibleWithAttribute`). The matrix is in `docs/design/mvp-ux.md` 1.4. Nothing else in the code checks a role name for this.

- **Responses.** `CostVisibilityBehavior` (a pipeline step after validation, for every request) runs `ResponseShaper` over the result: a tagged property of a caller without the permission is set to `null`, wherever it sits (nested objects, lists, pages). Tagged properties are therefore nullable `init` properties; `OnlyWhen` limits the rule to one case (an approval trigger hides its numbers only for an amount rule). Handlers see the full values: only the outermost request is shaped (`ShapingState`), so a handler that sends another command reads the complete result.
- **Audit.** `AuditPayloadScrubber` removes the keys that name a cost or value from the before/after JSON (entity properties tagged in the Domain, plus the payload keys of `AuditFields`); a payload that carries money must name its key with an `AuditFields` constant. The audit log itself is Owner and Admin only.
- **Excel.** Handlers write spreadsheets only through `IReportWriter` with `ReportColumn`s (`ReportHeaders`, `ImportTemplates`); a column with `VisibleWith` is left out, header and cells, for a caller without the permission.
- **Sorting and filtering.** An enum member tagged `[VisibleWith]` (`StockSort.Value`) is refused with `400` by `RequestGuard` for a caller without the permission, because the order would reveal the value.
- **Receipts without cost.** `IReceiptCostDefaults`: a receipt saved by a caller without `cost.view` takes the last known (average) cost per product in the warehouse, or zero for a product never stocked there, whatever cost the request carried (`StockDocument.ApplyKnownReceiptCosts`).
- **Tests.** `Ims.Architecture.Tests/CostVisibilityCoverageTests` fails when a response property, entity property, export column or sort key whose name looks like cost or value is not tagged, when a tagged property cannot be withheld, when anything but `ReportWriter` uses `IExcelWriter`, or when an audit payload names money without `AuditFields`. `Ims.Api.Tests/Tests/Visibility` asks every read, export and document command as every role and compares the money found (by name) with what the role may see.

## Costing and reversal

`StockBalance` holds quantity and the moving weighted-average cost (10 decimal places, `Limits.CostDecimals`, SQL `decimal(22,10)`, rounded half away from zero); a value is always `quantity x average`.
The cost per base unit and the average are INTERNAL numbers, not money: every amount that reaches a person is money (2 decimals, `Ims.Domain.Money`, which arrives with the money-precision change), and nothing in the API or an export shows the internal precision once the money-precision change is in.

- **Marker.** `StockBalance.LastReceiptLineId` is the document line that brought stock in last. EVERY stock-in sets it: a receipt line, a receipt at the average itself (a staff receipt takes the average as its cost) and the line of a reversed issue. An equal average is never taken as proof that nothing came in since.
- **Snapshot.** Posting a receipt line records `StockDocumentLine.AvgCostBefore`, `AvgCostAfter` and `PreviousLineId` (the marker before it).
- **Reversal** (`StockBalance.Unreceive`): while the line is still the marker, the average goes back to `AvgCostBefore` and the marker to `PreviousLineId`; otherwise the receipt's value (`qty x cost`) is taken out of the stock value and the rest is shared by the remaining units, and the marker is cleared (the recorded averages of the later receipts still contain this one); where that has no sound answer the average is kept; a first receipt whose units were issued under negative stock keeps the last known cost instead of 0. The result never exceeds `Limits.MaxCostPerBaseUnit`. The lines of one product are reversed last-posted first.
- **Range.** A unit cost is at most `Limits.MaxUnitCost` (1,000,000,000) when entered, and its cost per base unit at most `Limits.MaxCostPerBaseUnit` (999,999,999,999, the 12 integer digits of the column): a 400 (`CostTooLarge`, `CostTooLargeForUnit`), never a database overflow. `DocumentSaveRequestValidator`, the opening-stock import and `CostingPolicy` enforce it.
- **Maintenance window.** Migration `AverageCostMarkersAndCostScale` changes `StockLedgerEntry.UnitCost`, `StockDocumentLine.UnitCost` and `StockBalance.AvgCost` from `decimal(18,6)` to `decimal(22,10)` (the type's storage grows from 9 to 13 bytes) and adds four nullable columns. It rewrites the three tables under a schema-modification lock: about 3 minutes on 1.5 million ledger rows with a cold cache (independent measurement: ledger 128 s, document lines 64 s; Down about 21 s); plan a maintenance window of several minutes, scaled to the size of the ledger (a warm 1M-row LocalDB copy took 20 s, so the time depends on the cache and the disk). Stop the API; rebuild the indexes of the three tables afterwards (`docs/dba/07-maintenance.sql`). No value can overflow, because the integer part is 12 digits before and after. The migration test (`AverageCostMigrationTests`) runs Up, Down and Up again on a database that holds rows.
- **Repair.** `docs/dba/15-repair-reversal-average.sql` recomputes, once, the average of the balances that the old reversal left contaminated (report first, then `-v Apply=1`).
- `PostingEngine.FindReversalDeficits` still decides, by quantity, whether a reversal is allowed. The rule and its examples are in `docs/design/mvp-ux.md` 1.5; the tests are `ReceiptReversalCostingTests` (domain) and `ReceiptReversalCostTests` (API).

## Data, concurrency and performance rules

Standard audit columns on every table, soft delete, parameterised data access only, split queries, O(1)-minded design, ordered locks, timeouts and
physical file storage are specified in [data-and-performance-rules.md](data-and-performance-rules.md).

## Constants

No magic numbers or strings: `Ims.Domain/Common/Constants` (limits, audit names, messages, document-number format),
`Ims.Application/Common` (report and import labels), `Ims.Infrastructure` (config keys, SQL types, seed data), `Ims.Api` (routes, headers, log templates).

## Logging

Serilog: console + rolling daily file (30 days), structured, every line tagged with `CorrelationId` (`X-Correlation-Id` echoed to the client).
Request logging records the final mapped status; business rejections are warnings, unexpected failures are errors with the stack.

## Security cross-cutting (Ims.Api)

- `SecurityHeadersMiddleware` (first in the pipeline after forwarded headers/HSTS): defensive headers on every response, added at response start so errors and 429s carry them; an endpoint may override (attachments do). Values: `SecurityHeaders`.
- Sign-in transport: the refresh token is also an `HttpOnly; Secure; SameSite=Strict; Path=/api/auth` cookie (`RefreshTokenCookie`); a request that relies on the cookie must pass `CsrfGuard` (custom header + Origin/Referer in `Cors:Origins`). `AuthController` chooses body or cookie; Application handlers do not know about cookies. The deprecated body delivery is off by default (`Auth:ReturnRefreshTokenInBody=false`); the switch stays for a rollback. See `docs/security/refresh-cookie.md`.
- Ports for pluggable security adapters live in Application and get a safe default in Infrastructure (`IUploadScanner` / `NoOpUploadScanner`).
- Static web hosting headers (CSP etc.) are a proxy concern: `docs/security/web-headers.md`. Runtime scan: `docs/security/dast.md`.

## Trade-offs recorded

- Application references `Ardalis.Specification` (persistence-ignorant) but not EF Core.
- Namespaces inside a layer are flat per feature (`Ims.Application.Features.Catalog`) while folders carry the finer structure.
- Roles and the permission matrix are code (PRD 3.1). Per-customer roles are a Phase 2 change.
