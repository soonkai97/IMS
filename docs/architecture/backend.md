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

An entity a client addresses derives from `PublicEntity` (itself an `Entity`): besides the integer key it has a `PublicId`, a random GUID made when the object is created and never set from outside. The integer key stays the clustered key and the key of every foreign key; the public id is what leaves the system (decision and plan: [adr-guid-keys.md](adr-guid-keys.md)). Application code looks rows up with `ByPublicIdSpec` / `PublicIdsSpec` and translates lists with the scoped `IIdMap<T>` (one query per list). The API of an entity flips to the public id in the stacked PRs 4 to 7 (on the branch `feature/public-ids-integration`, merged to main once, when the web client follows in PR-8). **Flipped: Warehouse, Partner, Category, Uom (PR-4), User (PR-5), Product (PR-6), and StockDocument, Attachment and ApprovalRule (PR-7): every addressable entity.** For a flipped entity: its route is `{id:guid}` (`ApiRoutes.PublicId`), its DTO `id` is the `PublicId`, and every field, filter and list of another entity that names it (product `categoryId` / `baseUomId` / conversions, document `warehouseId` / `partnerId`, line `uomId`, stock, ledger, low-stock and movement filters, user and session `warehouseIds`) is a GUID. The translation is at the edge: a handler turns the public ids of a request or filter into integer ids with `IIdMap<T>` (`ToInternalOrNoRowAsync` for one filter value: an unknown id becomes `IdMapExtensions.NoRow`, which matches nothing, so a filter on an id that does not exist returns an empty page exactly as before; a body reference that does not exist is still a 400) and passes only integers to specifications, readers and the domain; responses project the id from the joined row (`row.Warehouse.PublicId`), never from the integer foreign key. By-id routes look the row up with `ByPublicIdSpec<T>`; an unknown id, and a warehouse outside the caller's scope, answer 404 alike. JWT claims and `ICurrentUser` keep integer ids (warehouses and the user: the token never carries a public id). Users: `UserInfo.id` (login, refresh, me), `UserDto.id`, `PUT /api/users/{id:guid}` and the document fields `createdById` / `submittedById` / `returnedById` are the user public id; the ownership and self-approval rules compare integers only (the document DTO keeps the integer keys `CreatedByKey` / `SubmittedByKey` as `[JsonIgnore]`, like `WarehouseKey`), so a public id a client has read gives no right over a document. Audit: `AppDbContext.AuditedByPublicId` lists the entities whose audit rows are named by `PublicId` (the other entities and their business events keep the integer key until their own flip PR adds them); rows written before a flip keep their integer `EntityId`, and the audit page and its entity-id filter are text, so they show and match both. Products: `GET`/`PUT /api/products/{id:guid}`, `ProductDto.id`, `DocumentLineDto.productId` / `DocumentLineInput.productId`, `StockRow` and `LowStockAlert.productId`, the `productId` filters of the stock list, ledger, movements and their exports, and the `productId` of the insufficient-stock and reversal-blocked problems are the product public id (the problem's id is given by `ShortageNamingBehavior`, the domain exception keeps the integer to itself). Filters resolve the id to the integer ONCE per request (one seek on `UX_Product_PublicId`) and the specifications use that integer in the same predicates as before; rows project `Product.PublicId` from the product they already join, never a look-up per row; `DocumentLineDto` keeps the integer product key as `[JsonIgnore] ProductKey` for the stock of an open document. Imports still resolve products by SKU. Documents: `GET`/`PUT /api/documents/{id:guid}` and every action route (`submit`, `approve`, `reject`, `cancel`, `post`, `reverse`) take the public id; `DocumentDto.id`, `DocumentListItem.id` (the approvals inbox is the same list), `LedgerRow.sourceDocId`, the `sourceDocId` ledger filter and `ImportResult.documentId` are the document public id. A document line has no id (`DocumentLineDto.id` is gone; the lines are in the order they were entered). The list item keeps the integer position as `[JsonIgnore] Key`, the value the seek cursor is made from (the public id has no order); `DocumentsPageSpec` projects `PublicId` and `Id` from the row it already reads, the ledger spec takes `SourceDocument.PublicId` from the join it already has for the number, and the `sourceDocId` filter is resolved to the integer ONCE per request (`IIdMap<StockDocument>`). Commands carry the public id and load the document with `DocumentForUpdateSpec(Guid)`; the ownership, self-approval and scope rules still compare integers. The document business events (`document.created`, ...) keep naming the document by its NUMBER (what a person searches the audit trail for); `ApprovalRule` is in `AuditedByPublicId` (its row events name it by public id); `StockDocument` and `Attachment` are not row-audited, so the attachment events carry the attachment public id explicitly (their payloads hold no owner id) and `AuditedByPublicId` has no entry for them. `ShortageNamingBehavior` names a shortage by SKU and warehouse name, and when a soft-deleted product or warehouse cannot be named it falls back to a message with their public ids (never the integer keys). Attachments: `AttachmentDto.id` and `ownerId` (list query, upload form, response) are public ids for BOTH owner types. The owner is resolved by its type (`IAttachmentOwnerPolicy.ResolveAsync`: the document table for `Document`, the product table for `Product`, one seek on `UX_*_PublicId`), after the permission check and before the existing scope and state checks (which still work on the integer key): an unknown id, an empty id, the id of a row of the other kind and an out-of-scope document are all the same 404 of that owner type, so a product id can no longer be told from a missing one by a 200 against a 404. Approval rules: `PUT /api/approval-rules/{id:guid}`, `ApprovalRuleDto.id`. The OpenAPI test `Every_id_on_the_wire_is_a_uuid_except_the_listed_positions` checks every property or parameter named `id` or ending in `Id`/`Ids` and fails for an integer unless it is one of the listed positions (`LedgerRow.id`, `AuditLogDto.id`, the text `entityId`). Storage details and the migration measurements are in [data-and-performance-rules.md](data-and-performance-rules.md) section 9.

## Permissions and roles

Design: [adr-roles-permissions.md](adr-roles-permissions.md) (lean scope). Built so far (slices RP-1 and RP-2; no change in what any role may do):

- **Catalog.** `Ims.Domain.PermissionCatalog` lists the 21 permissions: stable code, group, one-sentence description, dangerous flag, administrability-core flag, `Requires`. The `Permissions` constants name the codes; the old 15-code role-keyed map is gone (RP-4a).
- **Defaults.** `SystemRoleDefaults` holds what the five system roles get (the one place that names roles for rights); `RightsFor(role, userId)` is the shim that gives the Domain policies an `ActorRights` until roles come from the database (`ICurrentUser.Rights()` in Application).
- **One computation.** `EffectivePermissions.Compute(roleGrants)` keeps the codes the catalog knows; `ActorRights` is the user id plus that set.
- **Policies by permission.** `DocumentActionPolicy`, `ApprovalPolicy.MayReview(ActorRights, isLarge)` (tier permissions `document.approve` / `document.approve.large`) and `PostingAuthority` take `ActorRights`, never a role name: ownership is `document.edit.any`, posting an approved document `document.post.approved`, the negative-stock override `stock.negative.override`. `ApprovalRule.Tier` reads the tier from the role the rule still stores. The role-keyed `RankOf`, `MayReview(role, ...)` and `CanApprove` stay for the approval services until RP-4b.
- **Tables (slice RP-2).** `Role` (code, name, description, system, active; public id; soft delete; unique code and name among live rows) and `RolePermission` (role + permission code, unique per role among live rows), seeded with the five system roles and their `SystemRoleDefaults` by migration `AddRolesAndPermissions` (a frozen copy, so a fresh and an upgraded database hold the same rows). `User.RoleId` points at the user's role row and `User.AllWarehouses` holds the old "sees every warehouse" rule; both are written with the role string on every user save, and `ApprovalRule.Tier` is written with `RoleRequired` on every rule save. Only the access snapshot reads them (slice RP-3, below), and the endpoints decide by the permissions in the token it writes (RP-4a); the approval limit and the warehouse scope still read the role (RP-4b), so what any role may do did not change. Row-level audit covers `Role` (by public id) and `RolePermission`.
- **Rights in the token (slice RP-3).** The access token carries the rights, so nothing is looked up per request. `IAccessSnapshotLoader` reads one `AccessSnapshot` (role code and name, `EffectivePermissions`, `AllWarehouses`, warehouse ids) uncached at sign-in and at refresh, before any row is written; no snapshot (user missing or inactive, role deleted or inactive) is a 401 with no new refresh-token row, no rotation and no success audit. `TokenService` writes `sub`, `nameid`, `name`, `role` (display and audit only), one `perm` per permission, `allwh`, `wh` only when `allwh` is false, and `acv` (`ClaimNames.CurrentClaimsVersion`; `ConfigureJwtBearer` refuses a token whose `acv` is not the current one with 401, so the web refreshes silently after a release that changes the claims). The lifetime is 5 minutes (`Limits.AccessTokenMinutes`, `Jwt:AccessMinutes`). A change of role or scope applies at the next refresh (about 5.5 minutes with the clock skew); only a deactivation or a password change revokes refresh tokens, and the access token already issued runs to its expiry. `UserInfo` (sign-in, refresh) is built from the same snapshot and `/me` from the presented token's claims. There is no cache and no per-request read. **Enforcement (RP-4a):** one authorization policy per catalog code, named by the code (`RequireAssertion(HasClaim("perm", code))`), plus the any-of policies of `AuthPolicies` (`DocumentReview`: either approval tier; `DocumentCancel`: create or either tier); `ICurrentUser.Rights()` builds the `ActorRights` of the Domain policies from the same claims. The `role` claim decides no permission.
- **Proof.** `tests/Ims.Domain.Tests/Identity/Oracle` holds the frozen answers of the pre-roles code; `RightsParityTests` checks the new types against them for the five roles (documented differences: the Owner approves by the large tier, the six new codes, and two posting answers that only hold together with `document.post`). `EndpointCoverageTests` checks that every policy an endpoint asks for is in the catalog exactly once.

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
  The token is a keyed hash, never the raw `rowversion` (one counter for the whole database, which would let a warehouse-scoped user measure what other warehouses write; ADR guid-keys, D2): HMAC-SHA256 over the document's public id and its row version, first 16 bytes, base64url (`IDocumentVersion`, `DocumentVersion`). The same document version always gives the same string (the web compares tokens for equality), and a token of another document, an old 8-byte one or any garbage is the same 409 as a stale one. The key is HKDF-SHA256 of the symmetric JWT `SigningKey` with its own info string (`Ims.DocVersion.v1`), so there is no extra secret to manage. Consequences: `DocumentVersion` depends on that symmetric key, so a move to asymmetric JWT signing needs a separate key for it; rotating the signing key gives every open editor one 409 (all sessions end then anyway). An `If-Match` that names no version (`""`, only commas) is `428`, like a missing header; only `*` skips the check. Ledger and audit rows carry no id on the wire for the same reason; their internal key only feeds the encrypted cursor.
- A barcode lookup miss is `200 { found: false, product: null }`, not a 404 (a scan that matches nothing is a normal outcome).

## Cost and value visibility

Who may see cost and value is decided by two permissions, `cost.view` (held by default by Clerk, Supervisor, Owner, Admin) and `valuation.view` (by default Supervisor, Owner, Admin), and written once: a cost or value is tagged `[VisibleWith(permission)]` where it is declared (`Ims.Domain.VisibleWithAttribute`). The matrix is in `docs/design/mvp-ux.md` 1.4. Nothing else in the code checks a role name for this.

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
- Permissions are defined in code (the catalog, stable codes) and roles are configurable: the five roles of PRD 3.1 are system roles whose permission matrix is their *defaults*, not a fixed rule; custom roles and the move of roles into the database follow [adr-roles-permissions.md](adr-roles-permissions.md) (accepted 2026-10-16, lean scope). Since slice RP-4a the endpoints read the `perm` claims of the token; `tests/Ims.Domain.Tests/Identity/Oracle` pins the answers of the old role-keyed matrix so every slice can prove the default behaviour did not move (see "Permissions and roles").
