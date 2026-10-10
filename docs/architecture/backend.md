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
  Specifications/           ProductByCodeSpec.cs ...  (Ardalis.Specification: queries as named objects)
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
(open/closed: a new document type adds a strategy), `ApprovalPolicy`, `DocumentActionPolicy` (the `allowedActions` the UI shows),
`StockAvailabilityPolicy`, `CostingPolicy`. They are pure and unit-tested without a database.

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
- Sign-in transport: the refresh token is also an `HttpOnly; Secure; SameSite=Strict; Path=/api/auth` cookie (`RefreshTokenCookie`); a request that relies on the cookie must pass `CsrfGuard` (custom header + Origin/Referer in `Cors:Origins`). `AuthController` chooses body or cookie; Application handlers do not know about cookies. Switch `Auth:ReturnRefreshTokenInBody`. See `docs/security/refresh-cookie.md`.
- Ports for pluggable security adapters live in Application and get a safe default in Infrastructure (`IUploadScanner` / `NoOpUploadScanner`).
- Static web hosting headers (CSP etc.) are a proxy concern: `docs/security/web-headers.md`. Runtime scan: `docs/security/dast.md`.

## Trade-offs recorded

- Application references `Ardalis.Specification` (persistence-ignorant) but not EF Core.
- Namespaces inside a layer are flat per feature (`Ims.Application.Features.Catalog`) while folders carry the finer structure.
- Roles and the permission matrix are code (PRD 3.1). Per-customer roles are a Phase 2 change.
