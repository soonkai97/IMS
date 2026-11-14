# DDD roadmap (backend)

Status: proposal for the owner, 2026-11-14. Documentation only: no code changes with this document.
Binding rules this roadmap serves: `docs/team/ENGINEERING-PRACTICES.md` (test first, tactical DDD; PR #268). Layering, locks and the rest of the backend design stay as in [backend.md](backend.md).
Evidence was read from `origin/main` at `9396d68`. Counts of tests are counts of `[Fact]` and `[Theory]` attributes, not of expanded cases. Section 7 says what was not run.

## 0. Summary

1. The Domain is further along than "rules and specifications only". Entities already have private setters, factories and state-changing methods; the document state machines, the posting engine with one strategy per document type, the moving-average cost, the approval and action policies and five `IBusinessRule`s are all pure Domain code with tests. What is missing is the shared vocabulary: no `AggregateRoot`, no `IValueObject`, no `IDomainEvent` exist anywhere in `src`, `tests` or `docs`.
2. The gaps that matter: (a) four decimal scales (money 2, quantity 4, factor 6, cost per base unit 10) are all plain `decimal`, document numbers and reasons are plain `string`; (b) 51 explicit `audit.Record(...)` calls in 49 handler and service files build the audit payload outside the aggregate; (c) the submit, approve, reject, recall and cancel workflow is written three times (stock document, sales order, purchase order); (d) a dozen business decisions sit in handlers and Application services (section 1.1).
3. Plan: 26 slices in six waves, each one PR under about 800 changed lines and each starting with a failing test. Value objects first (Domain only, nothing else moves), then decisions out of handlers, then the event base and the first aggregate, then the dispatcher with audit as the first subscriber, then the posting path last.
4. Two findings change the plan against the owner's brief and need your eye (section 6): audit must run inside the transaction (AUTH-03), so "handled after the save" is two phases; and an EF value converter cannot translate the server-side sums and projections the money and stock queries use, so value objects reach the entities in two levels.
5. Not recommended: a `Percentage` value object (nothing in the code or the requirements uses a percentage; section 2.3), typed ids, a `DateRange` value object.

## 1. Current state

Verdicts: **Good** (a real domain model, keep), **Partly** (model with primitives or gaps), **Anemic** (decision lives outside the Domain), **Primitive** (rule exists, type does not).

### 1.1 Across the Domain

| Topic | Verdict | Evidence | Gap |
|---|---|---|---|
| Entity bases | Good | `Common/Entity.cs:4`, `Common/Models/PublicEntity.cs:8`, `Common/Models/AuditableEntity.cs:10`; `DomainEncapsulationTests` (no public setters, no public constructors, no public fields) | No root marker, no event list, no value-object marker |
| Business rules | Good | `Common/Rules/IBusinessRule.cs`, `BusinessRules.Ensure`; five rules: reason required, self-approval, valid lines, base unit locked, conversion locked | Handlers still compose their own `if`s (below) |
| Money | Partly | `Common/Models/Money.cs:17` is a static class (2 and 4 decimals, rounding, `IsExact`, `Amount`), 34 call sites in 23 files | Not a value; amounts stay `decimal` |
| Other scales | Primitive | `SqlTypes.cs`: quantity `decimal(18,4)`, cost `decimal(22,10)`, factor `decimal(18,6)`; rules scattered in `UnitConversion.cs:15`, `CostingPolicy.cs:13`, `StockBalance.cs:42`, `Product.cs:78` | One rule per scale, each re-implemented where used |
| Reasons | Primitive | `ReasonRequiredRule` (usually a required check, then a too-short check) plus a manual `Trim()`: 12 uses of `Messages.ReasonTooShort` in 5 files | No `Reason` type |
| Workflow | Anemic by duplication | `StockDocument.cs:268-397`, `SalesOrder.cs:123-226`, `PurchaseOrder.cs:124-227`: the same Submit, Approve, Reject, Recall, Cancel with the same fields | Three copies drift apart |
| Cross-aggregate references | Partly | Factories take whole aggregates (`StockDocument.cs:128,151,160`: `Warehouse`, `Partner`, `PurchaseOrder`, `SalesOrder`); navigation properties on roots (`StockDocument.cs:85-95`) | Practices say "reference by id"; navigations exist for read-side projections |
| Events | Missing | Nothing raised anywhere. Audit is 51 `audit.Record` calls (Sales 15, Purchasing 10, Documents 8, Auth 5, Roles 4, Users 2, Import 2, Attachments 2, Returns 1, Settings 1, Modules 1) | Payloads are built in handlers (`SubmitDocumentHandler.cs:36-44`, `Sales/Services/ListAudit.cs:6`) |
| Row version of a root | Anemic | `AppDbContext.cs:513-580`: five `Touch...` methods, one per aggregate, each bumps the root when a child changes | One rule written five times |

Business decisions found outside the Domain (each is a defect under practice DDD-2; the first failing test of the slice that moves it is named in section 4):

| Where | Decision | Moves to |
|---|---|---|
| `ReverseDocumentHandler.cs:24` | Is a posted document returnable (receipt, or issue for sale, and not itself a return)? It decides whether the `ret` lock is taken and a query is made | `StockDocument.CanBeReturned` (DDD-09) |
| `SubmitDocumentHandler.cs:24-35` | Short stock: refuse, or flag an override, and `Required || requiresOverride` | `StockAvailabilityPolicy` (DDD-09) |
| `ApproveDocumentHandler.cs:17`, `RejectDocumentHandler.cs:19` | Self-review allowed = setting and not a return | `StockDocument` (DDD-09) |
| `DocumentAccess.cs:43`, `PurchaseOrderAccess.cs:43`, `SalesOrderAccess.cs:45` | "Mine" rule, three copies, reading the persistence column `RecRevisor` | `DocumentOwnership` (DDD-09) |
| `UpdatePurchaseOrderHandler.cs:17`, `UpdateSalesOrderHandler.cs:18` | Not a Draft is a 409 here, while `EnsureEditable` in the aggregate throws a 400 for the same state | `IsEditable` bool, the handler keeps its 409 (DDD-09, trap: see section 3, rule 2) |
| `ApprovalEvaluator.cs:24-35` | Which sentence refuses a reviewer (owner only reviews large, above approval limit) | `ApprovalPolicy.ReviewRefusal` (DDD-07) |
| `UpdateApprovalRuleHandler.cs:15-23` | An order rule keeps its type; the last active rule of an order type cannot be switched off | `ApprovalRuleSetPolicy` (DDD-10) |
| `SwitchModuleHandler.cs:24-45` | Reason required, prerequisites, dependents, readiness checks | `ModuleSwitchPolicy` (DDD-14) |
| `SalesDeliveryPosting.cs:37-39`, `ReturnPosting.cs:26-39` | Fully delivered; over-delivery and over-return | Aggregates and quantity policies (DDD-17) |
| `UpdateUserHandler.cs:29-30`, `AdministrabilityGuard.cs:13` | Cannot lock yourself out; the system keeps one administrator | `AdministratorContinuityPolicy` (DDD-15) |
| `LoginHandler.cs` (`TryAsync`) | Which outcome and which audit event for unknown, inactive, locked, wrong password, no live role | `User.Authenticate` (DDD-24) |
| `DocumentLineBuilder.cs:19`, `IssueValuation.cs:8`, `ReceiptCostDefaults.cs:8` | When a typed cost is used; type guards that `StockDocument.cs:246,257` already hold | Delete the duplicates (DDD-09) |

### 1.2 Stock

| Element | Verdict | Evidence | Gap |
|---|---|---|---|
| `StockDocument` | Good model | `Stock/Models/StockDocument.cs:8`: private setters, four factories (`:128,151,160,173`), full state machine (`:268-397`), read-only lines (`:82`), `Link` invariant (`:104`) | No root type, no events; bool parameters (`:268,289`); number is a string with `""` as "unassigned" (`:13,197`); three nullable link keys instead of one `DocumentLink` field |
| `StockDocumentLine` | Partly | `Stock/Models/StockDocumentLine.cs:86` (validated factory), `:125` (`CreateReturn`) | Ten-parameter factory with three optional link numbers; four scales as `decimal` (`:7-60`); `RebaseTo` is public (`:139`) and changes a child from outside the root's methods (its only caller, `ReturnBaseAlignment.cs:32`, is in the same assembly) |
| `StockBalance` | Good invariants, primitive | `Stock/Models/StockBalance.cs:34-100`: `Receive`, `Issue`, `Unreceive` carry BR-03 and BR-05 | Class comment says "read model rebuilt from the ledger" (`:4`) while the strategies mutate it (`ReceiptPostingStrategy.cs:11`); it is the write-side balance under the (warehouse, product) lock; fix the wording |
| `StockLedgerEntry` | Good | `Stock/Models/StockLedgerEntry.cs:4`: append-only (BR-01), enforced in `AppDbContext.EnforceAppendOnly` and by trigger | Primitive quantities and costs |
| Posting | Good | `PostingEngine.cs:5` (strategy per type, ProductId order), `IssuePostingStrategy.cs`, `ReceiptPostingStrategy.cs`, `PostingContext.cs` | `PostingContext.OverrideUsed` is a mutable out-flag; nothing is raised when a document posts |
| Policies | Good | `DocumentActionPolicy`, `StockAvailabilityPolicy`, `CostingPolicy`, `LinkedReceiptPolicy`, `ReturnQuantities`, `ReturnBaseAlignment`, `LowStockRule` (all pure, tested) | String-typed actions (`LinkedReceiptPolicy.cs`), primitives in and out |
| Numbering | Partly | `DocumentNumber.cs:3` static helper, `DocumentSequence.cs:4`, `Documents/Services/DocumentNumbering.cs` | No type for a number, no parse; allocation (lock plus repository) correctly stays in Application |

### 1.3 Sales

| Element | Verdict | Evidence | Gap |
|---|---|---|---|
| `SalesOrder` | Good model | `Sales/Models/SalesOrder.cs:8`, workflow `:123-226`, lines frozen after approval, numbered 1..n (`:91`) | Copy of the purchase order workflow; the order does not know what was delivered (it is computed, `SalesOrderQuantities.cs`); `SalesOrderActionContext` carries the delivery facts in, which is the right pattern |
| `SalesOrderLine` | Partly | `Sales/Models/SalesOrderLine.cs:38` | Quantity, unit and base quantity checks repeated in three line types |
| `PriceList`, `PriceListItem` | Good | `Sales/Models/PriceList.cs:8` (`ReplaceItems` keeps rows, one price per product and unit), `PriceListItem.cs:4` | Price is `decimal`; uniqueness across lists (one default, one per customer, one name) is `PriceListUniqueness.cs` plus unique indexes, which is correct (needs a query) |
| Handlers | Anemic in part | `CancelSalesOrderHandler.cs:17-30`, `CloseSalesOrderHandler.cs:19-26` (open and posted deliveries under the `so` lock); `SalesDeliveryPosting.cs:37-47` (fully delivered, then closes the order and audits) | Process steps that belong with the order |

### 1.4 Purchasing

| Element | Verdict | Evidence | Gap |
|---|---|---|---|
| `PurchaseOrder`, `PurchaseOrderLine` | Good model, duplicate | `Purchasing/Models/PurchaseOrder.cs:9`, workflow `:124-227`; `PurchaseOrderQuantities`, `ReorderRule` (pure, tested) | As sales; `LinkedReceiptPosting` closes the order and audits |
| Handlers | Anemic in part | `CancelPurchaseOrderHandler.cs:17`, `ClosePurchaseOrderHandler.cs:19`, `UpdatePurchaseOrderHandler.cs:17`, `PurchaseOrderAccess.cs:28-43` | As sales |

### 1.5 Approvals

| Element | Verdict | Evidence | Gap |
|---|---|---|---|
| `ApprovalRule` | Good | `Approvals/Models/ApprovalRule.cs:4`: `Update` guards thresholds (`:31-34`), `Matches`, `Triggers` | Amount and quantity thresholds are `decimal` |
| `ApprovalPolicy` | Good | `Approvals/Services/ApprovalPolicy.cs`: routing by tier, never by role name (enforced by `NoRoleDecisionTests`) | `ForReturn` is a bool tuple; `isReturn` special cases repeated in four places (`ApprovalEvaluator.cs:10,30`, approve and reject handlers) |
| Rule set | Anemic | Invariants that span rules live in `UpdateApprovalRuleHandler.cs:15-23` and `Settings/Services/ApprovalRuleModules.cs:12-17` | No home in the Domain |

### 1.6 Catalog

| Element | Verdict | Evidence | Gap |
|---|---|---|---|
| `Product` | Good | `Catalog/Models/Product.cs:8`: owns barcodes and conversions, `ChangeBaseUnit` (BR-04), `SetConversions`, locked rules | `SafetyStock` and `Factor` are `decimal`; `Scaled` (`:78`) is a local copy of the factor rule |
| `UnitConversion`, `UomConversion`, `Uom`, `Category` | Good | `Catalog/Services/UnitConversion.cs:13` | Quantity rounding rule lives here instead of on a quantity |
| `ProductWriter` | Correct | `Catalog/Services/ProductWriter.cs:21-49` orchestrates SKU and barcode uniqueness through specifications | Nothing to move: needs queries |

### 1.7 Modules

| Element | Verdict | Evidence | Gap |
|---|---|---|---|
| Catalog and dependencies | Good | `Modules/Services/ModuleCatalog.cs`, `ModuleDependencies.cs` (pure) | |
| The switch | Anemic | `SwitchModuleHandler.cs:24-45`; state is `SystemSetting` rows `Module.<code>` = `"true"`/`"false"` (`SettingKeys.cs:11`, `SettingValues.cs`), read three ways (`ModuleSwitches.cs:14`, `SwitchModuleHandler.cs` `EnabledModulesAsync`, `SystemSettings.EnabledModules`) | No policy, no event; the cache `Invalidate()` is an inline call |

### 1.8 Identity

| Element | Verdict | Evidence | Gap |
|---|---|---|---|
| `Role` | Good | `Identity/Models/Role.cs:9`: system role locked, admin core, catalog-only permissions, `SetPermissions` returns what was added and removed | No events; audit built in handlers |
| `User` | Good | `Identity/Models/User.cs`: profile rules, warehouse scope, lockout counters (AUTH-01) | Login decision in `LoginHandler`; administrator continuity in Application |
| `EffectivePermissions`, `ActorRights`, `PermissionCatalog` | Good | Pure, tested (`RightsParityTests`, oracle tests) | |

### 1.9 Auditing

| Element | Verdict | Evidence | Gap |
|---|---|---|---|
| `AuditLog` | Good | `Auditing/Models/AuditLog.cs:4`: append-only, enforced twice | |
| Business audit | Anemic | `IAuditTrail.Record` (`Application/Common/IAuditTrail.cs`), `AuditTrail.cs:7` stages a row in the unit of work | Called from 49 files; payload built in handlers; tests scan those call sites by regex (`CostVisibilityCoverageTests.cs:132`), so a subscriber must keep the literal `audit.Record(` form |
| Row audit | Good, relevant to value objects | `AppDbContext.cs:421-425` audited entities (Product, ProductBarcode, Warehouse, Partner, Category, Uom, ApprovalRule, User, UomConversion, Role, RolePermission); `CollectAudit` reads `PropertyEntry.CurrentValue` (`:587-616`) | With a value converter `CurrentValue` is the value object, so the JSON of the audit row would change (section 2.7) |

Small entities (`Warehouse`, `Partner`, `Category`, `Uom`, `Attachment`, `RefreshToken`, `SystemSetting`) are Good: factories, `EnsureUsable`, guarded updates. They stay plain `PublicEntity`.

## 2. Target model

### 2.1 Building blocks

| Type | Where | What |
|---|---|---|
| `IValueObject` | `Ims.Domain/Common/Models` | Marker. Every implementer is a `readonly record struct` (default value must be valid) or a sealed record, immutable, created through a factory, compared by value |
| `IDomainEvent` | `Ims.Domain/Common/Models` | Marker. A sealed record named in the past tense; carries facts (ids, status before and after, reason, value objects), never an entity |
| `IAggregateRoot` | `Ims.Domain/Common/Models` | `DomainEvents` (read-only) and `ClearDomainEvents()`. Roots without a public id (`StockBalance`, `DocumentSequence`) implement it directly |
| `AggregateRoot` | `Ims.Domain/Common/Models` | `abstract class AggregateRoot : PublicEntity, IAggregateRoot` with a private list and a protected `Raise(IDomainEvent)`. The list is get-only, so EF does not map it (same pattern as `Lines => _lines`) |
| `IDomainEventHandler<TEvent>` | `Ims.Application/Common` | In-transaction subscriber (section 2.5) |
| `IAfterCommitHandler<TEvent>` | `Ims.Application/Common` | After-commit subscriber |
| `IDomainEventDispatcher` | `Ims.Application/Common`, implemented in `Ims.Infrastructure/Persistence` next to `UnitOfWork` | No new package: the project already has its own small `Dispatcher` for commands |

Domain layout: the layout test allows only `Models | Rules | Services | Exceptions | Constants` below an area (`SourceLayoutTests.cs:10`). DDD-06 adds `Events` to that list on purpose.

### 2.2 Aggregates, roots and invariants

Rule of the practices: one transaction changes one aggregate, unless a locked process (posting, reversal) says otherwise. Other aggregates are referenced by id.

| Root | Owns | Invariants it guards | Process outside it (needs a lock or a query) |
|---|---|---|---|
| `StockDocument` | `StockDocumentLine` | Figure 5 state machine; a posted document is never edited, only reversed; at most one link (order or original); line base quantity (BR-04) and cost rules (BR-05); number assigned once (BR-11); reasons (BR-02); no self-review (BR-07); a return is never itself returned | Posting and reversal: document + balances + ledger in one locked transaction (BR-12) |
| `StockBalance` | nothing (ledger entries are produced by the process) | Moving average, negative stock only with override (BR-03), reversal restores the average | Changed only by `PostingEngine` under the (warehouse, product) lock |
| `StockLedgerEntry` | | Append-only (BR-01). Not a root: an immutable record created by the posting process | |
| `DocumentSequence` | | One running number per (prefix, warehouse, month) (BR-11) | `LockDocumentSequenceAsync` |
| `SalesOrder` | `SalesOrderLine` | Workflow; lines numbered 1..n and frozen after approval; price above 0 to submit; closed as delivered or short with reason | Delivered quantity is computed from posted deliveries; cancel and close are decided under the `so` lock |
| `PurchaseOrder` | `PurchaseOrderLine` | Same shape | `po` lock |
| `PriceList` | `PriceListItem` | One price per product and unit; the default list has no customer; a customer list belongs to a customer partner; item cap | One default, one list per customer, one name: a specification plus the unique indexes |
| `ApprovalRule` | | Thresholds exact and non-negative; known tier | Cross-rule invariants (below) |
| `Product` | `ProductBarcode`, `UomConversion` | Base unit locked once movements exist (BR-04); conversions lead into the base unit; a conversion in use cannot change | SKU and barcode uniqueness: specifications |
| `Role` | `RolePermission` | System role locked; admin core; catalog-only permissions | `roles` lock |
| `User` | `UserWarehouse` | Profile, active role, warehouse scope, lockout (AUTH-01) | Administrator continuity (below) |
| `SystemSetting` | | Key and value; raises `ModuleSwitched` when the key is a module's row | `modules` lock |
| Others | | `Warehouse`, `Partner`, `Category`, `Uom`, `Attachment`, `RefreshToken`: small roots, no events planned | |

Two invariants span aggregates and stay domain services that receive facts, because they have no row to be a root:

- `ApprovalRuleSetPolicy` (DDD-10): the rules of one document type. "While the module is on, the last active rule of an order type cannot be switched off" and "an order rule keeps its type". The practices text names an "approval rule set" aggregate; this roadmap makes it a policy, not a persisted root, because the rules are addressed and versioned one by one (public id, row version) and a set root would have no row of its own. The handler keeps the `modules` lock and the count; the policy decides. Veto in section 6.
- `AdministratorContinuityPolicy` (DDD-15): "the system keeps at least one active administrator" and "you cannot lock yourself out", from the count of the other administrators.

`ModuleSwitchPolicy` (DDD-14) follows the same pattern for the module dependency graph.

Navigation properties stay as read-side plumbing until the owner decides section 6, item 1.

### 2.3 Value objects

All are `readonly record struct` unless stated, implement `IValueObject`, have only get accessors, and take their refusal sentences from the existing `Messages` constants (a new sentence is a contract change, section 3).

| Name | Rule | Scale and bounds | Stored in | Replaces |
|---|---|---|---|---|
| `Money` | Exactly 2 decimals; half away from zero; `Exact` refuses a fraction of a cent; `Rounded`; add, subtract, compare; `Of(Quantity, UnitCost)` is `round(q x c, 2)`; cap check against `Limits.MaxLineAmount` | `decimal(18,2)` (`SqlTypes.Money`) | Line amounts, entered unit cost, prices, thresholds | The static `Money`: its members stay with the same signatures, so the 34 call sites compile unchanged |
| `UnitCost` | Cost per base unit: not negative, at most `Limits.MaxCostPerBaseUnit`, kept at `Limits.CostDecimals` (10) inside, shown at 4; `Blend` is the weighted average of `StockBalance.Receive` | `decimal(22,10)` (`SqlTypes.Cost`) | `UnitCost`, `AvgCost`, `AvgCostBefore/After`, ledger cost | Decimals in `CostingPolicy`, `StockBalance`, `StockDocumentLine` |
| `Quantity` | 4 decimals, half away from zero; factories `Positive` (a line), `NonNegative`, `Signed` (balance and delta; negative stock is allowed, BR-03); `Times(ConversionFactor)`; at most `Limits.MaxBaseQuantity` | `decimal(18,4)` | `Qty`, `BaseQty`, `QtyOnHand`, `QtyDelta`, safety stock, thresholds | `UnitConversion.ToBase`, three copies of the line quantity checks |
| `LineQuantity` | Entered `Quantity`, unit id, base `Quantity`, built by one call on `Product` | three columns, so never a converter: domain signatures only (level 1, section 2.7) | `Qty` + `UomId` + `BaseQty` on three line types | The triple and its validation in `StockDocumentLine.cs:86`, `SalesOrderLine.cs:38`, `PurchaseOrderLine` |
| `ConversionFactor` | Above 0; 6 decimals | `decimal(18,6)` | `UomConversion.Factor` | `Product.Scaled`, factor checks |
| `DocumentNumber` | `prefix-warehouse-yyMM-nnnn`; `Create` from parts; `FromStored` is lenient and never throws (a stored value must always load); parts read from both ends because a warehouse code may contain `-`; `Unassigned` replaces `""`; at most 40 characters | `nvarchar` (`Limits.DocumentNoMaxLength`) | `No` on documents and orders | The static `DocumentNumber` (its `PrefixFor`, `YearMonthOf`, `Format` stay) |
| `Reason` | Trimmed, at least `Limits.ReasonMinLength`; the "required" sentence is passed by the caller, so each call site keeps its own message | `nvarchar` | Rejection, cancel, reversal, close and override reasons | The twelve reason checks |

Considered and not recommended, with the reason:

- `Percentage`: no discount, tax, margin or rate exists in the code, and the requirements put tax and accounting out of scope. Build it with the first requirement that needs one.
- `DateRange`: only query filters pair two dates (`Reports/Contracts/MovementFilter.cs:5`, `Audit/Contracts/AuditQueryFilter.cs:15`); no domain rule uses a range, and those types are read-side contracts.
- Typed ids (`UserId`, `WarehouseId`): `int userId` is in hundreds of signatures; the churn, EF key converters and DTO mapping cost far more than the bugs they would stop.
- `Sku`, `Email`, `Currency`: one site each, or no such concept in the code (there is no currency anywhere, so money is one currency).

### 2.4 Domain events

An event is raised by the aggregate method that makes the change, after the change is valid; a refused transition raises nothing. Today's `audit.Record` content defines what each event carries, so audit can be rebuilt from it without changing a row.

| Events | Raised by | Carries (at least) | In the transaction | After commit |
|---|---|---|---|---|
| `StockDocumentCreated`, `Submitted`, `Approved`, `Rejected`, `Recalled`, `Cancelled` | `StockDocument.AssignNumber` (create: the audit row names the document by number), `Submit`, `Approve`, `Reject`, `Recall`, `Cancel` | Number, status before and after, reason, flags that audit prints today | Audit | |
| `StockDocumentPosted` | `MarkPosted` (the engine passes `OverrideUsed`) | Number, status before and after, override used, `DocumentLink` | Audit; closing the linked order (as a domain call, section 4 DDD-17) | Hook for a later low-stock or notification subscriber (none exists today: do not build one) |
| `StockDocumentReversed` | `MarkReversed` | Number, status, reason | Audit | |
| Return posted | `StockDocumentPosted` whose link kind is Return | | | A separate type only when a subscriber would otherwise need an `if` on the kind |
| `SalesOrderSubmitted`, `Approved`, `Rejected`, `Recalled`, `Cancelled`, `Closed(kind)`, `Created`, `Updated` | `SalesOrder` methods | As today's audit payloads | Audit | |
| `PurchaseOrder...` | Mirror of the above | | Audit | |
| `PriceListCreated`, `Updated`, `CustomerSet`, `Deleted` | `PriceList` | Header summary, item count, the price changes (the aggregate captures the "before" because the replace overwrites it) | Audit (the subscriber resolves SKU and unit labels for the words; one batched lookup per save) | |
| `ModuleSwitched(code, before, after, reason, rolesRegaining)` | `SystemSetting` | As the audit payload of `SwitchModuleHandler.cs:41` | Audit | `IModuleSwitches.Invalidate()` |
| `RoleCreated`, `Updated`, `PermissionsChanged`, `Deleted`; `UserCreated`, `Updated`, `PasswordChanged` | `Role`, `User` | Added and removed codes, ids | Audit | |
| Login succeeded, failed, locked out | `User.Authenticate` (DDD-24) | Username (the actor is the person who signs in, not the current user) | Audit | |

Stay explicit on purpose: attachment upload and delete, the two imports and logout. They are not aggregate transitions (an import is a batch use case), so they keep `audit.Record`.

### 2.5 When subscribers run ("handled after the save")

AUTH-03 says an audit row cannot be lost. If audit ran after the commit, a crash between commit and handler would lose it. So there are two phases, and audit is in the first:

1. In-transaction handlers (`IDomainEventHandler<T>`): run inside `IUnitOfWork.SaveChangesAsync`, after the aggregates have finished changing and before the unit of work saves, so the audit rows they stage are written by the same save as today and commit or roll back with the business change. A handler failure rolls the whole transaction back, exactly as a failing `audit.Record` does today. Handlers may raise further events; the loop runs until none are left.
2. After-commit handlers (`IAfterCommitHandler<T>`): run once, after the outermost transaction commits, for effects that must never roll a business change back (cache invalidation, a future notification). A failure is logged and swallowed.

Rules that make it safe:

- Order: the dispatcher sorts events by a process-wide monotonic number assigned in `Raise`, not by the order of tracked entities (the change tracker gives no order), so audit rows are written in the order the domain raised them, which is the order of today's `audit.Record` calls. The delivery that closes its order (document posted, then order closed) is the test that pins it.
- Retry: `UnitOfWork.ExecuteAsync` clears the change tracker when it re-runs the work, which discards the aggregates and their events; nothing is delivered for a discarded attempt.
- Actor and time: the audit subscriber reads the current user and the clock when it handles the event (as `AuditTrail.Record` does now); it does not use an event time, so `At` does not change meaning.
- Location: subscribers live in Application (feature folder `EventHandlers`, added to the feature-layout test in DDD-11) and call the same `audit.Record(` literal, so the existing architecture tests keep scanning the payloads.

### 2.6 Repositories and specifications

- Keep `IRepository<T>` (staging writes) and `IReadRepository<T>` (read access through specifications, DTO projections). CQRS stays: the read side is not turned into aggregates.
- No new per-aggregate repository interface. The specification is the loading contract (`DocumentForUpdateSpec`, `SalesOrderForUpdateSpec`, `PriceListForUpdateSpec`); a new interface needs a reason a specification cannot give.
- A rule needs data: Application runs the specification and passes the answer to the Domain as a small immutable fact (the pattern `SalesOrderActionContext` already uses; examples to follow: the count of other administrators, the number of active rules of a type, the open deliveries of an order). The Domain never receives a repository.
- Write access `IRepository<T>` is for roots plus an explicit short list of append-only or infrastructure records (`StockLedgerEntry`, `AuditLog`, `RefreshToken`, `DocumentSequence`, `SystemSetting`), enforced by an architecture test (section 5). Today 20 types are injected as `IRepository<T>`; all but those five are roots.

### 2.7 How value objects reach the entities (two levels)

An EF value converter alone cannot carry a value object onto a column that SQL adds up, multiplies, searches or sorts, and the stock and sales queries do that. Sites found: `d.Lines.Sum(l => l.BaseQty)` and `Sum(l => l.Amount)` (`DocumentDtoSpec.cs:24`, `DocumentsPageSpec.cs:22`, `PurchaseOrderDtoSpec.cs:16`, `PurchaseOrdersPageSpec.cs:19`, `SalesOrderDtoSpec.cs:16`, `SalesOrdersPageSpec.cs:19`), the delivered, received and returned readers (`EfDeliveredQuantityReader.cs:20`, `EfReturnedQuantityReader.cs:20`, `EfReceivedQuantityReader.cs:14`; PR 267 pinned the first two with `QuantityReaderPlanTests`, the pattern DDD-18 and DDD-20 reuse), `Math.Round(b.QtyOnHand * b.AvgCost, Money.Decimals)` (`EfStockSummaryReader.cs:18`), `ReorderQuery.cs:48,54`. The PL3 timings in `docs/dba/README.md` section 19 depend on those plans.

So adoption has two levels:

- Level 1: domain signatures. Factories, aggregate methods, policies, strategies take and return value objects; the entity keeps its `decimal` or `string` property and converts at the boundary. This removes the primitive obsession the practices ask about, touches no query, no snapshot and no row-audit payload. Every slice outside the persistence track (DDD-18 to DDD-20 and DDD-22) stays at level 1.
- Level 2: the property itself becomes the value object, mapped to the same column. Allowed only where no server-side arithmetic, search or projection depends on the column, or where DDD-18 proves one of: (a) a converter with the read side on the primitive column, (b) an EF complex type (SQL identical, but the model snapshot text changes, which needs the owner, section 6 item 3), with `ToQueryString` equal to today's. Expected candidates: thresholds, price list prices, reasons, document numbers; the summed columns probably stay level 1 for good.

## 3. Hard constraints for every slice

Each rule names the existing test that proves it. A slice that needs to edit an existing assertion has changed behaviour: stop and bring it to the architect.

1. No endpoint or JSON contract change. `OpenApiDocumentTests`, `JsonDifferenceTests` (`Ims.Api.Tests/Tests/Contract`) pass unchanged. Value objects never appear on a request or response type (architecture test, section 5); mapping to `decimal` and `string` happens in Application.
2. No `ProblemCodes`, status code or message change. `ProblemDetailsTests`, `MessagesTests`, `ExceptionMessageTests` pass unchanged; no edit to `Messages.cs` (a value object reuses the sentences that exist). Trap: a handler that answers 409 for a state the aggregate answers with 400 (`UpdatePurchaseOrderHandler.cs:17` against `EnsureEditable`) keeps its status: the domain offers a question (`IsEditable`), the handler keeps its mapping and its place in the order of checks.
3. No column, migration or stored-data change. `SchemaShapeTests` ("pinned tables, keys..." and "the model has no change that a migration does not carry") pass; no file under `Persistence/Migrations` changes; the model snapshot is byte-identical (`git diff --exit-code` on it in the PR). The one exception, EF complex types, needs the owner (section 6).
4. Converters map to today's columns: same `HasColumnType` from `SqlTypes`, same rounding as SQL (the `MoneyPrecisionTests` pairing stays), and a round-trip property test through real SQL Server for each converted property (value in, same value out, same scale).
5. SQL parity for every query that touches an adopted column: a `ToQueryString()` test pins the statement of the listed sites in 2.7 before the slice and after.
6. Lock order and transaction behaviour unchanged. `ITransactionLocker` calls keep their order (`modules`, then `so` / `ret` / `po`, then document sequence, then document, then stock balances ascending); no domain method takes a lock; handlers keep their `uow.ExecuteAsync` boundaries and their retry; `ThePostingCode_NeverReadsAModuleSwitch` passes. A decision moved out of a handler must not move a lock with it (for example `ReverseDocumentHandler.cs:24` decides whether the `ret` lock is taken).
7. The audit trail behaves the same: same action, entity type, entity id, before and after JSON keys and values, actor and, within one request, row order; same atomicity (the row commits with the change). Each slice that touches an audit site first commits a characterization test written against `main` and green there. `[VisibleWith]` stays on the same property names (the scrubber works by name). Row audit of an entity that gets a value object must serialize the provider value (DDD-19), not the object.
8. Cost and value visibility the same. `CostVisibilityCoverageTests` and `ResponseShaper` tests pass unchanged; the `[VisibleWith]` tags stay on the entity properties the audit log reads.
9. Existing suites pass unchanged: `Ims.Domain.Tests` (804), `Ims.Architecture.Tests` (71), `Ims.Api.Tests` (1,601), including the stock oracle. Only additions, plus the named allow-list and layout-list edits of a slice card.
10. Row versions: the root still moves its `ROW_VERSION` when a child changes (the Touch rule), and an ETag still changes when only a line, item or grant changes.
11. Process: first commit of a slice is the failing test and the PR description says which test failed first; no drive-by rename; stop and split at about 800 changed lines; no new package.
12. Performance: no new query on a hot path. Where a card adds one (price list labels), the PL3 k6 scenario for that endpoint is rerun and the number quoted.

## 4. Slices

### 4.1 Overview

Sizes are estimates of changed lines with tests, not measurements. "Review" names where `deep-reviewer` on opus is required (touches posting, transactions, audit, row versions, authorization). Lanes follow `docs/team/PROTOCOL.md`: tests by unit-test-engineer or integration-test-engineer, code by be-framework (Persistence by be-dba), review by be-architect and deep-reviewer.

| ID | Slice | Est. lines | Needs | Run | Review |
|---|---|---|---|---|---|
| DDD-01 | Value-object marker and design test | 120 | | Serial gate | normal |
| DDD-02 | `Money` value object | 250 | 01 | Parallel group A | normal |
| DDD-03 | `Quantity` value object | 300 | 01 | A | normal; run money and posting suites once |
| DDD-04 | `DocumentNumber` value object | 250 | 01 | A | normal |
| DDD-05 | `UnitCost` and `ConversionFactor` | 350 | 01 | A | normal; run the oracle |
| DDD-06 | Event and aggregate base types; `SalesOrder` as first root (raises, nobody listens) | 450 | | A | normal |
| DDD-07 | Reviewer refusal into `ApprovalPolicy` | 250 | | A | opus (authorization) |
| DDD-08 | `Reason` value object | 300 | 01 | Serial on the three aggregate files | normal |
| DDD-09 | Small decisions into the Domain (returnable, self-review, ownership, editable, availability, duplicates) | 450 | 08 | Serial on the aggregate files | opus (reversal lock path, authorization) |
| DDD-10 | `ApprovalRuleSetPolicy` | 300 | | Parallel with 08 | normal |
| DDD-11 | Dispatcher and audit for `SalesOrder` (first subscriber) | 700 | 06 | Serial gate | opus (unit of work, audit) |
| DDD-12 | `PurchaseOrder` root, events, audit | 550 | 11, 09 | Parallel group B | opus (audit) |
| DDD-13 | `PriceList` root, events, audit | 550 | 11 | B | opus (audit) |
| DDD-14 | `ModuleSwitchPolicy` and `ModuleSwitched` | 450 | 11 | B | opus (audit, `modules` lock) |
| DDD-15 | `Role` and `User` events, `AdministratorContinuityPolicy` | 500 | 11 | B | opus (audit, authorization) |
| DDD-16 | `StockDocument` root, events, audit (all verbs except post and reverse) | 700 | 09, 11 | Serial on `StockDocument.cs` | opus (audit) |
| DDD-17 | Posting and reversal events; linked orders close through the aggregates | 800 | 12, 16 | Serial | opus plus be-system-designer (posting, locks, audit order) |
| DDD-18 | EF mapping spike and ADR (`Money` on `PriceListItem.ListPrice`) | 350 | 02, 03 | Parallel with the event track | opus (database shape) |
| DDD-19 | Row-audit provider values; value objects on the audited decimals | 350 | 18 | Serial after 18 | opus (audit) |
| DDD-20 | Order lines and price items adopt value objects (two PRs: sales, purchasing) | 600 | 18, 13 | After 18 | normal |
| DDD-21 | Posting path signatures: balance, ledger, strategies, context | 800 | 03, 05, 17 | Serial | opus (posting, money) |
| DDD-22 | `StockDocumentLine` and `No` adopt value objects | 700 | 21, 04 | Serial on `StockDocument.cs` | opus |
| DDD-23 | One "touch the root" rule instead of five | 300 | 19 | Serial on `AppDbContext.cs` | opus (row versions) |
| DDD-24 | Login decision into `User.Authenticate` | 400 | 15 | After 15 | opus (security) |
| DDD-25 | Gated: factories take facts, not aggregates | per aggregate, 300 to 500 | Owner decision | | opus |
| DDD-26 | Gated: one shared approval workflow for the three aggregates | 800 | Owner decision | | opus |

### 4.2 Order, parallelism and file gates

Waves, in order:

1. DDD-01 alone (it is small and the others use its marker).
2. Group A in parallel: 02, 03, 04, 05, 06, 07. Disjoint files; none edits `Messages.cs`.
3. Serial pair 08 then 09 (both edit the aggregate classes and the Access services), with 10 in parallel (Approvals only).
4. DDD-11 alone (it adds the dispatcher everything else uses).
5. Group B in parallel: 12, 13, 14, 15. Disjoint aggregates and handlers; they register subscribers by assembly scan, which DDD-11 must confirm (`AddAppServices` scans `IAppService`; subscribers get the same treatment), otherwise one line in `DependencyInjection.cs` conflicts. Then 16, then 17.
6. The persistence track (18, 19, 20, 21, 22, 23) may run beside waves 3 to 5 with different authors, honouring the gates below. 24 after 15.

Serial gates (touch the same file, so one at a time, in this order):

| File | Order |
|---|---|
| `Stock/Models/StockDocument.cs` | 08, 09, 16, 17, 22 |
| `Sales/Models/SalesOrder.cs` | 06, 08, 09, 17, 20 |
| `Purchasing/Models/PurchaseOrder.cs` | 08, 09, 12, 17, 20 |
| `Sales/Models/PriceList.cs`, `PriceListItem.cs` | 13, 20 |
| `Infrastructure/Persistence/AppDbContext.cs` | 18, 19, 23 |
| `Infrastructure/Persistence/UnitOfWork.cs` | 11, then 17 only |

Every slice is rolled back by reverting its PR: none changes data, a column or a contract. Where a slice replaces `audit.Record` calls (11 to 17), the revert puts the calls back and the characterization test proves both states write the same rows.

### 4.3 Slice cards

Each card: scope, first failing test, files, risk, rollback. "Rollback" is "revert the PR" unless noted.

**DDD-01 Value-object marker and design test.** Scope: the empty marker `IValueObject` and `ValueObjectDesignTests`: every implementer is a struct or sealed record, has no public or internal setter or `init`, no mutable field, no public mutating `void` method, and no EF or serialization attribute. First failing test: the detector test over fixture types in the test project (a class that is not sealed, a public setter, a mutable field) fails because the marker does not exist. Files: `Ims.Domain/Common/Models/IValueObject.cs`; `Ims.Architecture.Tests/ValueObjectDesignTests.cs`. Risk: none. The design test is vacuous until DDD-02 lands, which is why the fixture detector comes first.

**DDD-02 `Money`.** Scope: `Money` becomes a `readonly record struct` with the instance API of 2.3 and keeps every existing static member and constant with the same signature, so no call site changes. Also adds two ratchet tests (section 5: primitives in Domain signatures; no value object on a response). First failing test: `MoneyTests`: 12.5 equals 12.50 by value; `Exact(12.505)` refuses with the given sentence; `Rounded` is half away from zero, equal to the existing SQL parity cases. Files: `Common/Models/Money.cs`, `Ims.Domain.Tests/Common/MoneyTests.cs`, the two arch tests. Risk: low; run `MoneyPrecisionTests` and the money suites once on the final code.

**DDD-03 `Quantity`.** Scope: the type of 2.3; `UnitConversion.ToBase` delegates to it and still returns `decimal`. First failing test: a parity test over a few thousand generated (quantity, factor) pairs: `Quantity.Times(factor)` equals today's `UnitConversion.ToBase` to the last place; plus the `Signed`, `Positive` and `NonNegative` refusals. Files: `Common/Models/Quantity.cs`, `Catalog/Services/UnitConversion.cs`, `Catalog/UnitConversionTests.cs`, `Common/QuantityTests.cs`. Risk: low to medium: every line passes through the conversion.

**DDD-04 `DocumentNumber`.** Scope: the static helper becomes a value with the same static members. First failing test: round-trip theory (create, `ToString`, `FromStored`, read parts) including a warehouse code containing `-` and a running number above 9999; `FromStored("")` is `Unassigned`; a 41-character text is refused only on `Create`, never on `FromStored`. Files: `Stock/Services/DocumentNumber.cs` (path kept for blame), `Stock/DocumentNumberTests.cs`. Risk: low. `DocumentNumbering` is untouched.

**DDD-05 `UnitCost`, `ConversionFactor`.** Scope: the two types; `CostingPolicy.BaseUnitCost` and `Product.Scaled` use them; `StockBalance` still stores `decimal`. First failing test: `UnitCost.Blend` equals the average `StockBalance.Receive` produces for the cases of the stock oracle (`Stock/Oracle`), and `ConversionFactor.Scaled` equals `Product.Scaled`. Files: two new types, `CostingPolicy.cs`, `Product.cs`, tests. Risk: medium (costing is money): run the oracle suites and `ReceiptReversalCostingTests` once.

**DDD-06 Event and aggregate base types; `SalesOrder` first.** Scope: `IDomainEvent`, `IAggregateRoot`, `AggregateRoot`; add `Events` to the Domain layout list; `SalesOrder : AggregateRoot` raises `Created`, `Updated`, `Submitted`, `Approved`, `Rejected`, `Recalled`, `Cancelled`, `Closed` carrying exactly what the audit payloads print. Nothing listens. First failing test: `SalesOrderEventsTests`: submit with an approval rule raises one `SalesOrderSubmitted` (Draft to Submitted, requires approval, large); a refused transition raises nothing; `ClearDomainEvents` empties. Files: three base types, eight event files, `SalesOrder.cs`, `SourceLayoutTests.cs` (one list entry), new arch tests `EventDesignTests`, `AggregateRootTests` (section 5). Risk: low; `The_model_has_no_change_that_a_migration_does_not_carry` proves EF ignores the new get-only list.

**DDD-07 Reviewer refusal into the Domain.** Scope: `ApprovalPolicy.ReviewRefusal(rights, isLarge, rulesInForce, type, qty, amount, isReturn)` returns a small enum; `ApprovalEvaluator` maps it to the same sentence and the same 403. First failing test: a table test over rights x tier x rules x return, written first against `ApprovalEvaluator` (green: characterization), then pointed at the policy (red until it exists). Files: `Approvals/Services/ApprovalPolicy.cs`, `Application/Features/Documents/Services/ApprovalEvaluator.cs`, `Approvals/ApprovalPolicyTests.cs`. Risk: medium (authorization): opus review; the RBAC suites in `Ims.Api.Tests/Tests/Rbac` run once.

**DDD-08 `Reason`.** Scope: the type; the twelve reason checks in `StockDocument`, `SalesOrder`, `PurchaseOrder` and `SwitchModuleHandler.cs:26` use it; exception type and sentence unchanged; the stored string is the trimmed text as today. First failing test: `ReasonTests` pin the order (required sentence first, then too short), the trim, and the minimum; the existing `ReasonRulesTests` pass unchanged. Files: `Common/Models/Reason.cs`, the three aggregates, `SwitchModuleHandler.cs`, tests. Risk: low to medium (edits three aggregate files: serial gate). Adds the allow-list test `HandlersDoNotNewUpDomainRules` with the one remaining entry.

**DDD-09 Small decisions into the Domain.** Scope: `StockDocument.CanBeReturned`; `StockDocument` self-review rule; `DocumentOwnership` (one "mine" rule for three services; stops reading `RecRevisor` from Application); `IsEditable` on both orders (the handlers keep their 409); the short-stock decision in `StockAvailabilityPolicy`; delete the repeated type guards; make `StockDocumentLine.RebaseTo` internal. First failing tests, one per decision: a truth table for `CanBeReturned` (posted receipt: yes; posted issue for sale: yes; issue for use or scrap: no; a return: no; reversed or cancelled: no), the ownership table, the availability table (shortage with and without the negative-stock setting). Files: `StockDocument.cs`, `SalesOrder.cs`, `PurchaseOrder.cs`, `StockAvailabilityPolicy.cs`, new `DocumentOwnership.cs`, the three `*Access.cs`, four handlers, `StockDocumentLine.cs`. Risk: medium: the reverse handler's condition decides whether a lock and a query happen; the concurrency suites (`Tests/Concurrency`, returns) run once. If over 800 lines, split the reversal and ownership parts.

**DDD-10 `ApprovalRuleSetPolicy`.** Scope: the two cross-rule invariants of 2.2 as a pure policy taking facts (rule, new type, count of active rules of the type, module on) and returning a refusal enum; `ApprovalRuleModules.ModuleOf` and `IsOrderType` move to Domain. First failing test: truth tables from `UpdateApprovalRuleHandler.cs:15-23` (last active order rule; type change to or from an order type). Files: new `Approvals/Services/ApprovalRuleSetPolicy.cs`, `UpdateApprovalRuleHandler.cs`, `CreateApprovalRuleHandler.cs`, `ApprovalRuleModules.cs`, tests. Risk: medium: the `modules` lock and the count stay in the handler, in the same order.

**DDD-11 Dispatcher and audit for `SalesOrder`.** Scope: `IDomainEventHandler`, `IAfterCommitHandler`, `IDomainEventDispatcher`; the dispatcher in Infrastructure wired into `UnitOfWork.SaveChangesAsync` (before the save) and `ExecuteAsync` (after commit); `SalesOrderAuditSubscriber`; the eight `audit.Record` calls of the sales-order handlers go (create, update, submit, approve, reject, recall, cancel, close). The price-change audit (`SalesOrderPriceAudit.cs:24`) and the auto-close audit in `SalesDeliveryPosting` stay explicit; the second moves in DDD-17. Adds `EventHandlers` to the feature-layout test and the ratchet `AuditThroughEventsOnly` (baseline 51). First failing tests: (1) before any change, `SalesOrderAuditRowsTests` pins the audit rows of every sales-order verb on `main` (action, entity type, entity id, before, after, actor); (2) `DomainEventDispatchTests`: an event raised by a tracked aggregate reaches its handler once, inside the transaction; a handler that throws rolls the business change back; a retried attempt delivers nothing for the discarded one; an after-commit handler runs once after commit; the order of events is the order raised across two aggregates. Files: `Application/Common` (three interfaces), `Infrastructure/Persistence` (dispatcher, `UnitOfWork.cs`), `Application/Features/Sales/EventHandlers`, eight handler files, tests. Risk: high (shared framework behaviour, transactions, audit): opus plus be-system-designer. Rollback: revert; the characterization test passes on both sides.

**DDD-12 `PurchaseOrder`.** Scope: as DDD-06 and DDD-11 for the purchase order; the `PurchaseOrderClosed` audit in `LinkedReceiptPosting` stays explicit until DDD-17. First failing test: `PurchaseOrderAuditRowsTests` characterization first, then the events test. Files: `PurchaseOrder.cs`, event files, subscriber, eight handlers, tests. Risk: medium; opus for audit.

**DDD-13 `PriceList`.** Scope: root, four events, subscriber moved from `ListAudit.cs`; the aggregate captures the item prices before `ReplaceItems` overwrites them; the subscriber looks up SKUs and unit codes in one batched query. First failing test: the price-list audit rows (including the "changed from 3.00 to 3.50" words and the 50-line cap) pinned on `main`, then the events test. Files: `PriceList.cs`, event files, `ListAudit.cs` (moves), subscriber, four handlers, tests. Risk: medium: one added lookup per save; rerun the PL3 price-list k6 scenario and quote the figure.

**DDD-14 `ModuleSwitchPolicy` and `ModuleSwitched`.** Scope: the decision of `SwitchModuleHandler.cs:24-45` as a pure policy over facts (current state, enabled set, reason, readiness and orphan check results) returning an outcome or a refusal enum; `SystemSetting` raises `ModuleSwitched`; audit subscriber; after-commit `Invalidate`. The handler keeps the `modules` lock, the row read and write and the check collection. First failing test: the policy truth table, plus the existing module API tests unchanged. Files: `Modules/Services/ModuleSwitchPolicy.cs`, `SystemSetting.cs`, handler, subscriber, tests. Risk: medium; empties the `HandlersDoNotNewUpDomainRules` allow-list.

**DDD-15 Identity events and continuity.** Scope: `Role` and `User` become roots with the events of 2.4 and an audit subscriber; `AdministratorContinuityPolicy` replaces `AdministrabilityGuard` and the self-lockout check. The `roles` lock stays where it is. First failing test: audit rows of role and user verbs pinned on `main`; the policy table (last administrator, self lock-out). Files: `Role.cs`, `User.cs`, events, policy, `Roles` and `Users` handlers, subscriber, tests. Risk: medium to high (authorization, audit): opus.

**DDD-16 `StockDocument` root and events.** Scope: root, events for create, submit, approve, reject, recall, cancel; audit subscriber; the explicit calls of those verbs go (create is recorded in four handlers: document, from a purchase order, from a sales order, return; then submit, approve, reject, recall, cancel; post and reverse follow in 17). First failing test: audit rows of each verb pinned on `main`; events test. Files: `StockDocument.cs`, event files, subscriber, the document and order-creation handlers, tests. Risk: medium to high: opus. Serial gate on `StockDocument.cs`.

**DDD-17 Posting and reversal.** Scope: `StockDocumentPosted` (the engine passes `OverrideUsed`, so the context's mutable flag goes) and `StockDocumentReversed`; `SalesOrder` and `PurchaseOrder` get a method that decides "fully delivered or received" from facts and raises `Closed`, replacing the decision in `SalesDeliveryPosting.cs:37-47` and the purchasing twin; the over-delivery and over-return checks stay in the policies and are called from the aggregates' methods. Lock order and the read-under-lock stay in the link services exactly as today. First failing test: the delivery that closes its order writes document-posted then order-closed in that order, and the reversal writes its row; pinned on `main` first. Files: `StockDocument.cs`, `PostingEngine.cs`, `PostingContext.cs`, order aggregates, link services, `PostDocumentHandler.cs`, `ReverseDocumentHandler.cs`, subscribers, tests. Risk: highest (posting, locks, audit order). opus plus be-system-designer; run the full suites once and the concurrency tests. If over 800 lines, split posting from reversal.

**DDD-18 EF mapping spike and ADR.** Scope: `ValueObjectConverters` in `Infrastructure/Persistence`; `Money` adopted on `PriceListItem.ListPrice` only; ADR `docs/architecture/adr-value-objects-persistence.md` with the decision table of 2.7 per column family (converter; converter plus read-side primitive column; complex type; stay at level 1) and the evidence. First failing test: the `ToQueryString()` equality test of the price-list reader query and the real-SQL round trip. Files: converters, `AppDbContext.cs` (one line), `PriceListItem.cs`, tests, ADR. Risk: medium (database shape, no change expected): opus plus be-dba. Stop condition: the snapshot text changes, or any statement changes: record it and ask the owner (section 6) instead of continuing.

**DDD-19 Row-audit provider values.** Scope: `CollectAudit` and `After` serialize the provider value of a converted property; `ApprovalRule` thresholds, `Product.SafetyStock` and `UomConversion.Factor` adopt value objects per the ADR. First failing test: the audit payload of an approval-rule change, a product update and a conversion change pinned on `main` (green), run again with the value objects adopted (red until the fix). Files: `AppDbContext.cs`, three entities, tests. Risk: high (audit): opus.

**DDD-20 Order lines and price items.** Scope: `SalesOrderLine`, `PurchaseOrderLine`, `PriceListItem` and `LineQuantity` per the ADR (level 1 for the summed columns). First failing test: the `ToQueryString()` tests of the six spec sites in 2.7 for orders. Two PRs: sales, purchasing. Risk: medium.

**DDD-21 Posting path.** Scope: `StockBalance`, `StockLedgerEntry`, the two strategies, `PostingContext`, `CostingPolicy` signatures take `Quantity` and `UnitCost`; properties keep `decimal` (level 1). First failing test: the whole stock oracle and `ReferenceParityTests` stay green with the new signatures (they are the net); plus an explicit test that the reversal restores the average to the last place. Files: the Stock models and services and their tests. Risk: highest for money: opus; run the full Domain and the stock API suites.

**DDD-22 `StockDocumentLine` and `No`.** Scope: `LineQuantity`, `Money`, `UnitCost` in the line factories; `DocumentNumber` on `AssignNumber` (and the level 2 conversion of `No` only if the ADR allows it, since `d.No` is searched server-side by `EfDocumentTextMatcher`). First failing test: `ToQueryString()` of the document list and the text search. Risk: high: opus. Serial gate.

**DDD-23 One "touch the root" rule.** Scope: the five `Touch...` methods (`AppDbContext.cs:513-580`) become one rule driven by the roots' declared children. First failing test: an API test per aggregate kind that edits only a line, item or grant and expects a new ETag (some exist; add the missing), run first on `main`. Risk: high (row versions): opus plus be-dba.

**DDD-24 Login decision.** Scope: `User.Authenticate(passwordOk, now)` returns an outcome (unknown handled by the handler, inactive, locked, wrong password, locked out now, ok); the dummy-hash timing equalisation and the no-enumeration messages stay in Application. First failing test: a table test of every combination against today's behaviour (audit action, error, counter and lockout changes). Risk: high (security): opus.

**DDD-25 (gated) Factories take facts, not aggregates.** Scope, if the owner wants the strict reading of "by id": `StockDocument.Create(Warehouse, Partner)` and the three order factories take small facts (`WarehouseRef`, `PartnerRef`, `OrderRef`) instead of aggregates, per aggregate; navigation properties stay for read models. Adds the ratchet `PublicDomainMethodsTakeNoOtherAggregate`.

**DDD-26 (gated) One approval workflow.** Scope: extract Submit, Approve, Reject, Recall, Cancel from the three aggregates into one composed workflow object. Only on a trigger: a fourth workflow, or a rule change that has to be written three times again. Until then the duplication is cheaper than the abstraction.

## 5. Architecture tests to add

Added to `Ims.Architecture.Tests`; each states the slice that adds it. A ratchet starts at today's count and may only go down; its allow-list entry names a slice that removes it.

| Test | Rule | Slice |
|---|---|---|
| `ValueObjectDesignTests` | Every `IValueObject` is a struct or sealed record; no public or internal setter or `init`; no mutable field; no public mutating method; no persistence or serialization attribute | 01 |
| `ValueObjectsStayOffTheWire` | No value object type appears in `Application/Features/*/Contracts` or in `Ims.Api` | 02 |
| `PrimitiveRatchet` | Public Domain members with a `decimal` parameter or return whose name matches quantity, cost, price, amount, threshold or factor: committed baseline, may only fall | 02, lowered by 21 and 22 |
| `Domain_UsesNoPersistenceAttributes` | No `Microsoft.EntityFrameworkCore`, `Ardalis` or `System.ComponentModel.DataAnnotations.Schema` namespace in Domain (the project-reference test cannot see `[NotMapped]`, which ships with the base library) | 06 |
| `EventDesignTests` | Every `IDomainEvent` is a sealed record in Domain; no property of an entity type; the name is on a committed list (the past tense cannot be checked by reflection) | 06 |
| `EventsAreRaisedOnlyByTheirAggregate` | `Raise(` appears only inside the files of aggregate roots | 06 |
| `AggregateRootTests` | An explicit list of roots derives from `AggregateRoot` or implements `IAggregateRoot`; every other entity is a child of a root or on the short record list | 06, extended by each root |
| `ChildMutatorsAreInternal` | A child entity (`StockDocumentLine`, `SalesOrderLine`, `PurchaseOrderLine`, `PriceListItem`, `UomConversion`, `ProductBarcode`, `RolePermission`, `UserWarehouse`) has no public mutating method; allow-list empty (today `RebaseTo`) | 09 |
| `HandlersDoNotNewUpDomainRules` | No `new *Rule(` or `new *Policy(` in `Ims.Application`; allow-list: the one site in `SwitchModuleHandler.cs:26` | 08, emptied by 14 |
| `NoBusinessDecisionInHandlers` | In handlers and non-specification Application services, no `if`, conditional or `switch` that reads `.Status`, `.Type` or `.Purpose` of a Domain entity; a ratchet with the sites of section 1.1 as the allow-list (heuristic, syntax-based; false positives go on the list with a reason) | 09, shrinks with every slice |
| `DecisionsDoNotReadPersistenceColumns` | No read of `RecRevisor`, `RecCreator`, `RecCreDate`, `RecRevDate` in Domain or Application decisions; allow-list: `DocumentVersion.cs:39` (stale-document answer) | 09 |
| `AuditThroughEventsOnly` | Count of `audit.Record(` in `Features/*/UseCases` and services: baseline 51; final allow-list limited to import, attachment and logout, each with its reason | 11, shrinks |
| `RepositoriesOnlyForRoots` | `IRepository<T>` type arguments are roots or `StockLedgerEntry`, `AuditLog`, `RefreshToken`, `DocumentSequence`, `SystemSetting` | 11 |
| `PublicDomainMethodsTakeNoOtherAggregate` | Only if section 6 item 1 is answered "strict"; baseline from the factories | 25 |

Already in place and kept: Domain references nothing but the base library (`LayerDependencyTests`), entities have no public setters, constructors or fields (`DomainEncapsulationTests`), classes are sealed (`SealedClassesTests`), no direct clock reads (`ClockUsageTests`), no role-name decisions (`NoRoleDecisionTests`), posting never reads a module switch (`ModuleGateTests`), handler constructors take abstractions (`HandlerDesignTests`).

## 6. Open owner decisions

Only the ones the architect cannot settle alone. Defaults apply until you answer.

1. How strict is "reference other aggregates by id"? Today aggregates carry navigation properties and factories take whole aggregates. Strict (DDD-25: facts at the factories, navigations kept only for read models) costs 300 to 500 lines per aggregate and touches posting-adjacent code; loose leaves it as is and adds nothing to the model's safety. Default: loose, revisit after DDD-17.
2. Is "approval rule set" a policy, not a persisted root, acceptable? The practices text says aggregate; the rules have their own rows and row versions, so a set root would have no row. Default: policy (DDD-10); the practices wording gets amended.
3. May the model snapshot text change? Only if DDD-18 finds that EF complex types are the one way to carry a value object onto a summed column with identical SQL. The DDL and the data stay the same; `AppDbContextModelSnapshot.cs` would differ. Default: no; those columns stay at level 1 (domain signatures only).
4. Is level 1 an acceptable "done" for the columns SQL adds up (line amounts, quantities, balances, costs)? The practices ask for value objects in domain signatures (done) and "stored through converters" (not possible there without a query rewrite). Default: yes.
5. Percentage: confirm it stays out until a requirement brings the first one.

Not a decision but a cost to plan for: DDD-11 to DDD-17, DDD-21 and DDD-22 need `deep-reviewer` on opus and be-system-designer; and `StockDocument.cs` is a serial gate, so feature work on documents in the same weeks will conflict.

## 7. Not verified

- No code, test or build was run for this document. Nothing in `src` or `tests` changed.
- Every claim about code is a reading of `origin/main` at `9396d68` (file and line cited). Line numbers drift with the next merge; the slice author re-greps.
- EF behaviour (a converter cannot translate `Sum`, member access or `Contains` on the converted property; an EF 10 complex type can; a converter leaves the snapshot unchanged) is stated from knowledge of EF Core, not proved here. DDD-18 exists to prove it before anything depends on it.
- Slice sizes are estimates from the number of call sites and the size of the files named, not measured.
- `docs/team/ENGINEERING-PRACTICES.md` was read from PR #268 and again after it merged (this branch merges main).
- Test counts are attribute counts (`[Fact]`, `[Theory]`), so they differ from the counts in `TASKS.md` T18, which were stated in PR reports.
