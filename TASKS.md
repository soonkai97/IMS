# IMS — Task Breakdown (from PRD v0.2)

Stack: Angular 22 (PWA) · .NET 10 (Clean Architecture, CQRS) · SQL Server.
Scope of this pass: **Phase 0 (Foundation) + Phase 1 (MVP)**. Phases 2–4 are backlog at the bottom.
Legend: [x] done and verified · [~] in progress · [ ] not started · [-] deferred or dropped (lean scope)

## Phase 0 — Foundation
- [x] T01 Solution scaffold: Domain / Application / Infrastructure / Api / Tests
- [x] T02 EF Core + SQL Server, migrations (append-only triggers, READ_COMMITTED_SNAPSHOT), seed
- [x] T03 AUTH-01 Login, JWT + refresh-token rotation, 5-failure lockout
- [x] T04 AUTH-02 RBAC by feature + warehouse scope (403 on API)
- [x] T05 AUTH-03 Immutable audit log (row-level + business events)
- [x] T06 OpenAPI document + Swagger UI (`/swagger`)
- [x] T07 Angular 22 PWA scaffold, Nx 23, four tiers (shared, core, features, containers), `app-*` wrappers, auth, i18n (en/zh/ms)

## Phase 1 — MVP (backend)
- [x] T08 MD-01/02/03 Products (+ barcodes, unit conversions), warehouses, partners (deactivate-only)
- [x] T09 MD-05 Excel import (products, opening stock) with preview + per-row errors, templates
- [x] T10 Document state machine, numbering (BR-11), rowversion
- [x] T11 Append-only ledger + balance read model + concurrency-safe posting, deadlock policy (BR-01, BR-12)
- [x] T12 IN-01/02 Receipts
- [x] T13 OUT-01/02/03 Issues: stock check, negative-stock override, approval rules, no self-approval
- [x] T14 Reversal of posted documents (BR-02)
- [x] T15 STK-01/02 Stock enquiry + ledger
- [x] T16 Low-stock list
- [x] T17 RPT-01 Reports and Excel exports
- [x] T18 Tests: 459 domain unit, 51 architecture, 629 API integration on real SQL Server (CI service container), smoke test in CI
- [x] T19 Frontend screens (64 Jest tests). Open: attachments panel, "Mine" filter (API gap), E2E
- [ ] T20 End-to-end verification with UI (behaviour tests, QA personas)

## Quality
- [x] Q1 Architecture: Clean Architecture, CQRS (commands/queries + dispatcher + pipeline), vertical slices, one type per file
- [x] Q2 Rich domain model (behaviour, factories, business rules, domain services)
- [x] Q3 Constants and messages centralised, no magic numbers/strings
- [x] Q4 Logging (Serilog, correlation id), health checks, ProblemDetails
- [x] Q5 Deadlock policy (ordered locks, optimistic concurrency, retry on 1205, RCSI) — proven by 24-way parallel postings
- [x] Q6 Independent architecture review → docs/reviews/backend-initial/
- [x] Q7 DBA performance review at 1M ledger rows → docs/dba/README.md (indexes shipped; R3 movements ordering in progress)

- [x] Q8 Standard audit columns on every table (REC_DELETED, REC_CREATOR, REC_CREDATE, REC_REVISOR, REC_REVDATE, ROW_VERSION), soft delete, filtered unique indexes
- [x] Q9 Physical file storage for images/documents (attachments), type by content, owner policies
- [x] Q10 Server-side paging everywhere, seek paging for large lists, OFFSET depth cap
- [x] Q11 Security review fixes (lockout concurrency, change password, import zip-bomb check, PDF active content, release gated on CI, demo seeding local only)
- [x] Q12 Git workflow guards: `.githooks` (pre-commit, commit-msg, pre-push) + `scripts/guard/*`, `pr-guard.yml`, `.github/rulesets/main.json`, `docs/team/GIT-WORKFLOW.md`. Setup per clone: `node scripts/setup-git-hooks.mjs`

- [x] Q13 Security hardening, API side (`docs/security/`): SEC-34 refresh token in an HttpOnly cookie + CSRF defence (backward compatible, `Auth:ReturnRefreshTokenInBody`), security response headers on every response (SEC-24), image metadata removal and `IUploadScanner` hook (SEC-18), `web-headers.md` (Caddy/nginx/CSP), weekly ZAP API scan `dast.yml`
- [ ] Q14 Web app moves to the refresh cookie (steps: `docs/security/refresh-cookie.md` section 5), then set `Auth__ReturnRefreshTokenInBody=false`; apply the header set of `docs/security/web-headers.md` on the static host; plug a real AV engine into `IUploadScanner`; triage the first DAST run

## Delivery
- [x] T21 CI/CD: `ci.yml` (build, unit, architecture, integration on SQL container, smoke, web on Node 22, audit), `pr-guard.yml`, `codeql.yml`, `dast.yml` (weekly ZAP scan), `release.yml` (image to GHCR after green CI), `dependabot.yml`. Open: `deploy.yml` is a gated skeleton, hosting target undecided (PRD 14.3)
- [x] T22 Repository https://github.com/soonkai97/IMS (public, `main` protected by ruleset: PR + 4 required checks, squash only)
- [~] T23 Reusable base: rename script + `docs/TEMPLATE.md` done; framework extraction (Framework.* packages, npm lib) after a second project exists

## Roadmap epics (lean scope, approved 2026-10-12)
Scope: [docs/design/lean-scope.md](docs/design/lean-scope.md) (32 frontend slices, about 17,600 lines, 16 waves, backend track in its section 3.4; deferred items with triggers in its section 4, dropped items in section 5). Schedule, migration order, client batches, risks and decisions: [docs/pm/ROADMAP.md](docs/pm/ROADMAP.md) version 2. Totals: about 415 agent-hours (about 520 with 25% rework), about 38 working days (range 30-50) counted from Monday 12 Oct 2026; M6 about Wed 2 Dec 2026. Review checklist on every PR: "Which stated requirement needs this?" ([PR-REVIEW section 8](docs/team/PR-REVIEW.md)). A deferred item is built only after the project-manager records its trigger in HANDOFFS.
- [~] EP01 Money and QA backend fixes (M1): #44 average cost on reversal (merging), #45 money with exactly 2 decimals (fix and review), #51 replacement for #46 (per-warehouse low stock, uncapped search, ledger balance; fix and review; #46 to be closed as superseded). Merge in that order; migrations MG1, MG2 (ROADMAP 3.2). About 16 h left
- [ ] EP02 Backend hardening: lean = bug and security items only (ER-06, ER-07, ER-08, ER-14, ER-21, SEC-08), 12 h. [-] deferred: B1-B3 Home filters, B18 unlock, B20 text limits
- [ ] EP03 Document corrections (M3): lean = recall (Edit button) and cancel on Approved with a reason; B21, B22 lean, R28, R31; no migration expected. [design](docs/design/document-corrections.md), lean-scope 6.3. About 20 h. [-] deferred: send back. Needs the 3 owner confirmations (defaults exist)
- [ ] EP04 Public GUID ids, hybrid (M3): ADR accepted. PR-0..PR-3 on main (migration MG3 `AddPublicIds`), PR-4..PR-8 on `feature/public-ids` (no migration there), PR-9 one merge with the `large-change` label, PR-10 IDOR retest. Includes web slices G01, G02. [ADR](docs/architecture/adr-guid-keys.md), lean-scope 6.2. About 74 h. [-] deferred: `Partner.Code` and migration MG4
- [~] EP05 UI refresh, lean (M1, M2, M4, M6): 25 slices R (18, incl. HOME), F1, L00/L02, Q1, A02/TB/A07 (lean-scope 3.1), about 13,400 lines, about 90 h with the lean motion set. [x] R01 palette (#49 merged), [x] R02 required marker (#50 merged); next L00, R27, R03, R33. [-] deferred or dropped: lean-scope 4 and 5 (R12, R16, R18-R20, R22, R23, A01, A03 resize/reorder, A04, A06, A08-A22, Q02-Q12, L01, L03-L09). [plan v2](docs/design/2026-refresh-plan.md) is the full reference only; [sign-in](docs/design/2026-refresh-login.md)
- [ ] EP06 Configurable roles and permissions, backend (M5): lean = RP-0..RP-6 and RP-8, simplified (lean-scope 6.1; migration MG5 `AddRolesAndPermissions`). RP-0..RP-2 may merge before the id switch, RP-3 onward only after PR-9. [ADR](docs/architecture/adr-roles-permissions.md) defaults taken by the lean approval. About 120 h with review. [-] deferred: RP-7 per-user exceptions. [-] dropped: RP-9 impact preview. Done: RP-5 (roles API: permissions, roles list/get/create/update/permissions replace/delete with containment, the in-use refusal under the `roles` lock, audit events; the audit soft-delete row proved for roles in #109), RP-0..RP-3 (#102: claims in a 5-minute token), RP-4a (#104: enforcement from the `perm` claims, the `acv` check, the Owner's either-tier approvals; static 15-code map deleted), RP-4b (a role is data: migration `DropLegacyRoleColumns` drops `User.Role` and `ApprovalRule.RoleRequired`, the approval limit is by tier with no rank of role names so an Administrator-only rule is satisfied by the Owner too (Q5), `User.AllWarehouses` is a choice per person (`allWarehouses` in the user save), the user validators ask the Role table, `Roles` is `SystemRoleCodes`). RP-8 done (`NoRoleDecisionTests`: the build fails on `IsInRole`, `[Authorize(Roles)]`, the role claim or a system-role name outside short allow-lists with reasons; the last-administrator guard was the one remaining role-name rule, RP-6 replaced it). Web part of RP-4b done (regenerated api-client with `roleName`, `seesAllWarehouses`, `allWarehouses`, `approverTier`; `$seesAllWarehouses` from the user info; a "Sees every warehouse" checkbox in the Users form; the Settings rule form sends `approverTier`, no Administrator option). Left: web slices (P04: the Settings list and the document approval text still print the constant `roleRequired` as the approver; spec for the large-only `$selfApprovalBlocked`), cleanup (an Administrator saved with allwh off cannot manage users that have warehouses; 8-parameter `ToInfoAsync`, `SystemRoleDefaults.RightsFor` read only by tests, the `Roles` using alias in the test projects, a role dropdown from the roles API instead of `ALL_ROLES`), RP-6 (needs P04 first: two screens print the constant `roleRequired` as the approver, false once a role can lose its grants; two parallel demotions of the last two administrators by a non-administrator leave zero administrators until the `access:admin` lock; also the last-administrator guard by the administrability core; a user of a soft-deleted role can no longer arise, RP-5 refuses deleting or deactivating a role that users hold under the `roles` lock); RP-5 follow-ups (reviews of #118, recorded not built): refused role attempts (403/409) write no audit row, role responses carry no `Cache-Control: no-store`, `ETag` is not exposed over CORS, `RoleVersion` is a copy of `DocumentVersion` (extract at a third use), closure under `Requires` lives only in the validator (move into `Role` with RP-6 if cheap), `GET /api/roles` has no cap (paging with the roles web slices), the `role_in_use`, `system_role_protected` and `role_name_exists` problem codes of the ADR are not emitted (status and message only); load runs need `Jwt__AccessMinutes=30` (tests/load/README.md).
- [ ] EP07 Roles screens (M4, M5): RP00 profile helper, RP01, RP02, RP03, RP05+RP11 (about 3,000 lines, 22 h). Done: CS5 (client regenerated, #122), RP01 (read-only Roles list; the menu entry follows `roles.read`; "Users and roles" is now "Users"), RP02a (a role opens from the list as a view-only page: permissions by section with the API sentence, locked role explained). Next, in order: RP02b (create, edit, delete a role, branch `feature/roles-web-rp02-full` holds it). [-] deferred or dropped: RP04, RP06-RP10. [UX](docs/design/roles-permissions-ux.md) is the full reference
- [ ] Stylelint config bug (found in the #123 review, not fixed there): `web/.stylelintrc.json` lists `declaration-property-value-disallowed-list` twice (lines 5 and 11), so the second key replaces the first and the `white-space: nowrap` rule is dead. Fixing it may flag existing code; its own small PR (fe-framework).
- [-] EP08 Motion language: replaced by the lean motion set inside F1, R08, R10, R13, R25 (lean-scope 2.5); M02, M04, M06, M07, M08 dropped. [spec](docs/design/2026-refresh-motion.md) is the full reference
- [ ] EP09 Deployment and security follow-ups (M7, spare slot): hosting decision (owner), `deploy.yml`, Q14 (web on the refresh cookie, headers, AV hook, DAST triage). About 30 h
- [ ] EP10 Release gate: 4 QA rounds (three personas, one at a time), 3 gates, native review of zh and ms (stays `TODO-review` until named), R24 after soak. Folds in T20. About 30 h

Milestones (lean, roadmap section 6): [ ] M1 calm palette, money and quantity basics (WD 6, 19 Oct) · [ ] M2 new look and tables (WD 12, 27 Oct) · [ ] M3 corrections and public ids (WD 20, 6 Nov) · [ ] M4 Home, filters, phone (WD 25, 13 Nov) · [ ] M5 configurable roles (WD 33, 25 Nov) · [ ] M6 lean refresh complete (WD 38, 2 Dec) · [ ] M7 deployment (about WD 40, depends on hosting). Acceptance checks: roadmap section 6.

## Team (agents in `.claude/agents/`, protocol in `docs/team/PROTOCOL.md`, PR review in `docs/team/PR-REVIEW.md`)
19 roles: security-engineer, product-manager, business-analyst, project-manager, ui-ux-designer, fe-designer, fe-architect, fe-framework,
be-architect, be-framework, be-system-designer, be-dba, data-flow-analyst, unit-test-engineer, integration-test-engineer,
behavior-test-engineer, qa-novice-warehouse-staff, qa-power-supervisor, qa-edge-accessibility.
Every role can review a PR (`node scripts/pr-roles.mjs main` picks the reviewers). They hand off through `docs/team/HANDOFFS.md`; the main
session routes. New agent types register after a session reload; until then they run as general-purpose agents that load their definition file.

## Backlog (not in this pass)
- Phase 2: scanning (IScanInput), stocktake, transfers, purchase orders, offline sync, push, print templates, UoM conversion UI, label printing
- Phase 3: accounting export, API keys/webhooks, e-commerce, OCR drafts, reorder suggestions
- Phase 4: batch/expiry, serial, multi-company, BOM/FIFO, RFID, NL queries, owner dashboard

## Review follow-ups (minor, none blocking; found 2026-10-12)
- #57: typing "abc" then deleting to "ab" shows the unfiltered list while the box still reads "ab" (fix the comment or add a 3-to-2 spec); `API_MIN_SEARCH_LENGTH` sits under the wrong comment in `pagination.const.ts`.
- #57: the ledger "balance after" column and the document "Reversed by" line have no screen yet (client fields exist); needs a designer look, about 100 lines.
- #55 accepted gap: no horizontal scroll at 1280 and the `15-reports-range` e2e are deferred to slice L04 (table column priorities).
- #55 minors not done: cancel an in-flight request when the range turns invalid; `format.pipes.ts` holds five pipes (one type per file).
- #52: `docs/dba/08-reconcile-drill.sql` check 3 uses `ISNULL` sentinels (-1, -2); two practically impossible cases are missed.
- TB follow-up (not built; fe-designer ruling): when the top of a table is above the viewport after a page or size change, call `scrollIntoView({ block: 'start' })` on the table host, using a `scroll-margin-top` equal to the top bar, never `.focus()`; `behavior: 'auto'` (instant) under reduced motion.
- e2e is not a PR gate (found in the #121 review: CI was green while `15-containment` had a regression). Proposal for the owner: make the key specs (`10-phone`, `11-accessibility`, `12-low-stock`, `13-cost-visibility`, `15-containment`, `31-table-toolbar`) a required CI job on a clean database, or at least a nightly run that notifies on failure; CI is not changed here.
- Run the full e2e suite once after F1 and R04 merge (it runs only manually and weekly); watch `12-low-stock` ("New receipt line never appeared" once).
- Deferred: backend text limits (partner 120, product 80); `includeTotal=false` once a screen shows no total; Dependabot retries for stylelint 17 (needs `stylelint-config-standard-scss` support) and the .NET group (Microsoft.CodeAnalysis 5.9.0 conflict).
- Owner spot-check: on a phone, tap a quantity field on a receipt and type; the value should be replaced (R27, untested on real iOS and Android).

## Review follow-ups, round 2 (minor, none blocking; found 2026-10-12)
- #60 nits: line amounts of about 90 trillion and above lose cent precision in a double (the API allows 1,000 trillion); `lineAmount` throws on NaN/Infinity if called directly. Chrome rewrites pasted "1,5" to 15 before the badInput check runs (a paste handler is a follow-up); the footer total skips blank-cost lines, so "Items (n)" and "n lines" can disagree.
- #63 minors (RP00): four components still call `has()` for cost/valuation; Home `$canApprove` and the document editor test only `document.approve` (the Owner loses Home's approvals card once the API gives the Owner only `document.approve.large`: do it in RP01); `ApprovalsStore` still calls `SystemSettingsStore.ensureLoaded()` for nothing; the role-name lint guard misses template literals and concatenation; no test for the approvals route guard accepting the large tier.
- #65 minors (R08): the Saved state widens short labels (Save 67 -> 105 px) and has no `aria-disabled`; the zh/ms "Saved" strings need native review.
- #68 minors (R10): Chrome leaves a control that is already in view but 17-26 px under the sticky action bar (Remark box); the bar-height watcher needs `ResizeObserver`; the keyboard-aware tab bar is not built (iOS Safari untested, needs a real-iPhone QA round); the tabpanel wraps only the table, not the filters.
- GUID plan: warehouse-id probing on import (two messages: "no access" vs "unknown", by warehouse code) is not fixed by GUIDs; a deactivated admin's access token works until it expires (pre-existing); the documents list INNER JOINs `[User]` through the soft-delete filter; k6 scenarios and `e2e/src/api.ts` still assume numeric ids (PR-2b); the audit history of flipped entities mixes integer and GUID ids; add the general "every id is uuid" OpenAPI test (PR-10); optional `Database:CommandTimeoutSeconds` setting for very large migrations; run `docker compose up` once before the first deploy (key ring volume, never tested).
- #105 follow-ups (security review): purge `RefreshToken` rows that are revoked or expired for more than N days (every rotation adds a row, and the 300-per-minute refresh ceiling lets one session add them about 15 times faster than before); key the rate limits on the IPv6 /64 prefix instead of the full address (`Caller()` in `PresentationServices.cs`; a client holding a /64 can rotate addresses to get fresh sign-in and refresh buckets).
- #120 follow-up (fe-architect review): `DocumentsStore.counts` and `StockStore.summary` are root singletons and keep the previous user's numbers after sign-out (sign-out is a router navigation, no reload), the same pattern that leaked drafts through the Home feed store (fixed there by providing it per component). Follow-up: reset every root store on `AuthService.sessionEnded# IMS — Task Breakdown (from PRD v0.2)

Stack: Angular 22 (PWA) · .NET 10 (Clean Architecture, CQRS) · SQL Server.
Scope of this pass: **Phase 0 (Foundation) + Phase 1 (MVP)**. Phases 2–4 are backlog at the bottom.
Legend: [x] done and verified · [~] in progress · [ ] not started · [-] deferred or dropped (lean scope)

## Phase 0 — Foundation
- [x] T01 Solution scaffold: Domain / Application / Infrastructure / Api / Tests
- [x] T02 EF Core + SQL Server, migrations (append-only triggers, READ_COMMITTED_SNAPSHOT), seed
- [x] T03 AUTH-01 Login, JWT + refresh-token rotation, 5-failure lockout
- [x] T04 AUTH-02 RBAC by feature + warehouse scope (403 on API)
- [x] T05 AUTH-03 Immutable audit log (row-level + business events)
- [x] T06 OpenAPI document + Swagger UI (`/swagger`)
- [x] T07 Angular 22 PWA scaffold, Nx 23, four tiers (shared, core, features, containers), `app-*` wrappers, auth, i18n (en/zh/ms)

## Phase 1 — MVP (backend)
- [x] T08 MD-01/02/03 Products (+ barcodes, unit conversions), warehouses, partners (deactivate-only)
- [x] T09 MD-05 Excel import (products, opening stock) with preview + per-row errors, templates
- [x] T10 Document state machine, numbering (BR-11), rowversion
- [x] T11 Append-only ledger + balance read model + concurrency-safe posting, deadlock policy (BR-01, BR-12)
- [x] T12 IN-01/02 Receipts
- [x] T13 OUT-01/02/03 Issues: stock check, negative-stock override, approval rules, no self-approval
- [x] T14 Reversal of posted documents (BR-02)
- [x] T15 STK-01/02 Stock enquiry + ledger
- [x] T16 Low-stock list
- [x] T17 RPT-01 Reports and Excel exports
- [x] T18 Tests: 459 domain unit, 51 architecture, 629 API integration on real SQL Server (CI service container), smoke test in CI
- [x] T19 Frontend screens (64 Jest tests). Open: attachments panel, "Mine" filter (API gap), E2E
- [ ] T20 End-to-end verification with UI (behaviour tests, QA personas)

## Quality
- [x] Q1 Architecture: Clean Architecture, CQRS (commands/queries + dispatcher + pipeline), vertical slices, one type per file
- [x] Q2 Rich domain model (behaviour, factories, business rules, domain services)
- [x] Q3 Constants and messages centralised, no magic numbers/strings
- [x] Q4 Logging (Serilog, correlation id), health checks, ProblemDetails
- [x] Q5 Deadlock policy (ordered locks, optimistic concurrency, retry on 1205, RCSI) — proven by 24-way parallel postings
- [x] Q6 Independent architecture review → docs/reviews/backend-initial/
- [x] Q7 DBA performance review at 1M ledger rows → docs/dba/README.md (indexes shipped; R3 movements ordering in progress)

- [x] Q8 Standard audit columns on every table (REC_DELETED, REC_CREATOR, REC_CREDATE, REC_REVISOR, REC_REVDATE, ROW_VERSION), soft delete, filtered unique indexes
- [x] Q9 Physical file storage for images/documents (attachments), type by content, owner policies
- [x] Q10 Server-side paging everywhere, seek paging for large lists, OFFSET depth cap
- [x] Q11 Security review fixes (lockout concurrency, change password, import zip-bomb check, PDF active content, release gated on CI, demo seeding local only)
- [x] Q12 Git workflow guards: `.githooks` (pre-commit, commit-msg, pre-push) + `scripts/guard/*`, `pr-guard.yml`, `.github/rulesets/main.json`, `docs/team/GIT-WORKFLOW.md`. Setup per clone: `node scripts/setup-git-hooks.mjs`

- [x] Q13 Security hardening, API side (`docs/security/`): SEC-34 refresh token in an HttpOnly cookie + CSRF defence (backward compatible, `Auth:ReturnRefreshTokenInBody`), security response headers on every response (SEC-24), image metadata removal and `IUploadScanner` hook (SEC-18), `web-headers.md` (Caddy/nginx/CSP), weekly ZAP API scan `dast.yml`
- [ ] Q14 Web app moves to the refresh cookie (steps: `docs/security/refresh-cookie.md` section 5), then set `Auth__ReturnRefreshTokenInBody=false`; apply the header set of `docs/security/web-headers.md` on the static host; plug a real AV engine into `IUploadScanner`; triage the first DAST run

## Delivery
- [x] T21 CI/CD: `ci.yml` (build, unit, architecture, integration on SQL container, smoke, web on Node 22, audit), `pr-guard.yml`, `codeql.yml`, `dast.yml` (weekly ZAP scan), `release.yml` (image to GHCR after green CI), `dependabot.yml`. Open: `deploy.yml` is a gated skeleton, hosting target undecided (PRD 14.3)
- [x] T22 Repository https://github.com/soonkai97/IMS (public, `main` protected by ruleset: PR + 4 required checks, squash only)
- [~] T23 Reusable base: rename script + `docs/TEMPLATE.md` done; framework extraction (Framework.* packages, npm lib) after a second project exists

## Roadmap epics (lean scope, approved 2026-10-12)
Scope: [docs/design/lean-scope.md](docs/design/lean-scope.md) (32 frontend slices, about 17,600 lines, 16 waves, backend track in its section 3.4; deferred items with triggers in its section 4, dropped items in section 5). Schedule, migration order, client batches, risks and decisions: [docs/pm/ROADMAP.md](docs/pm/ROADMAP.md) version 2. Totals: about 415 agent-hours (about 520 with 25% rework), about 38 working days (range 30-50) counted from Monday 12 Oct 2026; M6 about Wed 2 Dec 2026. Review checklist on every PR: "Which stated requirement needs this?" ([PR-REVIEW section 8](docs/team/PR-REVIEW.md)). A deferred item is built only after the project-manager records its trigger in HANDOFFS.
- [~] EP01 Money and QA backend fixes (M1): #44 average cost on reversal (merging), #45 money with exactly 2 decimals (fix and review), #51 replacement for #46 (per-warehouse low stock, uncapped search, ledger balance; fix and review; #46 to be closed as superseded). Merge in that order; migrations MG1, MG2 (ROADMAP 3.2). About 16 h left
- [ ] EP02 Backend hardening: lean = bug and security items only (ER-06, ER-07, ER-08, ER-14, ER-21, SEC-08), 12 h. [-] deferred: B1-B3 Home filters, B18 unlock, B20 text limits
- [ ] EP03 Document corrections (M3): lean = recall (Edit button) and cancel on Approved with a reason; B21, B22 lean, R28, R31; no migration expected. [design](docs/design/document-corrections.md), lean-scope 6.3. About 20 h. [-] deferred: send back. Needs the 3 owner confirmations (defaults exist)
- [ ] EP04 Public GUID ids, hybrid (M3): ADR accepted. PR-0..PR-3 on main (migration MG3 `AddPublicIds`), PR-4..PR-8 on `feature/public-ids` (no migration there), PR-9 one merge with the `large-change` label, PR-10 IDOR retest. Includes web slices G01, G02. [ADR](docs/architecture/adr-guid-keys.md), lean-scope 6.2. About 74 h. [-] deferred: `Partner.Code` and migration MG4
- [~] EP05 UI refresh, lean (M1, M2, M4, M6): 25 slices R (18, incl. HOME), F1, L00/L02, Q1, A02/TB/A07 (lean-scope 3.1), about 13,400 lines, about 90 h with the lean motion set. [x] R01 palette (#49 merged), [x] R02 required marker (#50 merged); next L00, R27, R03, R33. [-] deferred or dropped: lean-scope 4 and 5 (R12, R16, R18-R20, R22, R23, A01, A03 resize/reorder, A04, A06, A08-A22, Q02-Q12, L01, L03-L09). [plan v2](docs/design/2026-refresh-plan.md) is the full reference only; [sign-in](docs/design/2026-refresh-login.md)
- [ ] EP06 Configurable roles and permissions, backend (M5): lean = RP-0..RP-6 and RP-8, simplified (lean-scope 6.1; migration MG5 `AddRolesAndPermissions`). RP-0..RP-2 may merge before the id switch, RP-3 onward only after PR-9. [ADR](docs/architecture/adr-roles-permissions.md) defaults taken by the lean approval. About 120 h with review. [-] deferred: RP-7 per-user exceptions. [-] dropped: RP-9 impact preview. Done: RP-0..RP-3 (#102: claims in a 5-minute token), RP-4a (#104: enforcement from the `perm` claims, the `acv` check, the Owner's either-tier approvals; static 15-code map deleted), RP-4b (a role is data: migration `DropLegacyRoleColumns` drops `User.Role` and `ApprovalRule.RoleRequired`, the approval limit is by tier with no rank of role names so an Administrator-only rule is satisfied by the Owner too (Q5), `User.AllWarehouses` is a choice per person (`allWarehouses` in the user save), the user validators ask the Role table, `Roles` is `SystemRoleCodes`). RP-8 done (`NoRoleDecisionTests`: the build fails on `IsInRole`, `[Authorize(Roles)]`, the role claim or a system-role name outside short allow-lists with reasons; the last-administrator guard was the one remaining role-name rule, RP-6 replaced it). Web part of RP-4b done (regenerated api-client with `roleName`, `seesAllWarehouses`, `allWarehouses`, `approverTier`; `$seesAllWarehouses` from the user info; a "Sees every warehouse" checkbox in the Users form; the Settings rule form sends `approverTier`, no Administrator option). Left: web slices (P04: the Settings list and the document approval text still print the constant `roleRequired` as the approver; spec for the large-only `$selfApprovalBlocked`), cleanup (an Administrator saved with allwh off cannot manage users that have warehouses; 8-parameter `ToInfoAsync`, `SystemRoleDefaults.RightsFor` read only by tests, the `Roles` using alias in the test projects, a role dropdown from the roles API instead of `ALL_ROLES`), RP-5 (roles API; MUST NOT ship before role writes have the same containment as the user save and a role in use cannot be deleted or deactivated, ADR section 10), RP-6 done (the last-administrator guard asks for the whole administration core, not the Admin name: `AdministrabilityGuard` + `ActiveAdministratorsSpec`, race-free under the `roles` lock taken first in the user save and the permission replace, 25-round parallel-demotion tests; a user of a soft-deleted role stays listed and can be moved; the `roleRequired` request field is gone, `ApproverTierNames.RoleOf` stays for the response text until the web stops printing it: P04 web part, still open, the two screens print a constant that is false once a role can lose its grants). RP-6 follow-ups: contract drift (`approverTier` is required at runtime but nullable in the OpenAPI; the committed OpenAPI and api-client still list `roleRequired`: regenerate in the next named batch; a PWA bundle from before #108 now gets 400); the Roles feature calls the `internal` `AdministrabilityGuard` of the Users feature (move it to a shared place); users of an inactive or deleted role show `isActive: true` but cannot sign in; a refused write (409 `LastAdminRequired`, `LockBusy`) writes no audit row; a deactivated administrator's token administers for up to about 5.5 minutes (the documented staleness of rights in the token); the roles lock now answers a clean 409 `LockBusy` after 5 s (`LockTimeoutException`, not retried) and the password hash is computed before the transaction; load runs need `Jwt__AccessMinutes=30` (tests/load/README.md).
- [ ] EP07 Roles screens (M4, M5): RP00 profile helper, RP01, RP02, RP03, RP05+RP11 (about 3,000 lines, 22 h). Done: CS5 (client regenerated, #122), RP01 (read-only Roles list; the menu entry follows `roles.read`; "Users and roles" is now "Users"), RP02a (a role opens from the list as a view-only page: permissions by section with the API sentence, locked role explained). Next, in order: RP02b (create, edit, delete a role, branch `feature/roles-web-rp02-full` holds it). [-] deferred or dropped: RP04, RP06-RP10. [UX](docs/design/roles-permissions-ux.md) is the full reference
- [-] EP08 Motion language: replaced by the lean motion set inside F1, R08, R10, R13, R25 (lean-scope 2.5); M02, M04, M06, M07, M08 dropped. [spec](docs/design/2026-refresh-motion.md) is the full reference
- [ ] EP09 Deployment and security follow-ups (M7, spare slot): hosting decision (owner), `deploy.yml`, Q14 (web on the refresh cookie, headers, AV hook, DAST triage). About 30 h
- [ ] EP10 Release gate: 4 QA rounds (three personas, one at a time), 3 gates, native review of zh and ms (stays `TODO-review` until named), R24 after soak. Folds in T20. About 30 h

Milestones (lean, roadmap section 6): [ ] M1 calm palette, money and quantity basics (WD 6, 19 Oct) · [ ] M2 new look and tables (WD 12, 27 Oct) · [ ] M3 corrections and public ids (WD 20, 6 Nov) · [ ] M4 Home, filters, phone (WD 25, 13 Nov) · [ ] M5 configurable roles (WD 33, 25 Nov) · [ ] M6 lean refresh complete (WD 38, 2 Dec) · [ ] M7 deployment (about WD 40, depends on hosting). Acceptance checks: roadmap section 6.

## Team (agents in `.claude/agents/`, protocol in `docs/team/PROTOCOL.md`, PR review in `docs/team/PR-REVIEW.md`)
19 roles: security-engineer, product-manager, business-analyst, project-manager, ui-ux-designer, fe-designer, fe-architect, fe-framework,
be-architect, be-framework, be-system-designer, be-dba, data-flow-analyst, unit-test-engineer, integration-test-engineer,
behavior-test-engineer, qa-novice-warehouse-staff, qa-power-supervisor, qa-edge-accessibility.
Every role can review a PR (`node scripts/pr-roles.mjs main` picks the reviewers). They hand off through `docs/team/HANDOFFS.md`; the main
session routes. New agent types register after a session reload; until then they run as general-purpose agents that load their definition file.

## Backlog (not in this pass)
- Phase 2: scanning (IScanInput), stocktake, transfers, purchase orders, offline sync, push, print templates, UoM conversion UI, label printing
- Phase 3: accounting export, API keys/webhooks, e-commerce, OCR drafts, reorder suggestions
- Phase 4: batch/expiry, serial, multi-company, BOM/FIFO, RFID, NL queries, owner dashboard

## Review follow-ups (minor, none blocking; found 2026-10-12)
- #57: typing "abc" then deleting to "ab" shows the unfiltered list while the box still reads "ab" (fix the comment or add a 3-to-2 spec); `API_MIN_SEARCH_LENGTH` sits under the wrong comment in `pagination.const.ts`.
- #57: the ledger "balance after" column and the document "Reversed by" line have no screen yet (client fields exist); needs a designer look, about 100 lines.
- #55 accepted gap: no horizontal scroll at 1280 and the `15-reports-range` e2e are deferred to slice L04 (table column priorities).
- #55 minors not done: cancel an in-flight request when the range turns invalid; `format.pipes.ts` holds five pipes (one type per file).
- #52: `docs/dba/08-reconcile-drill.sql` check 3 uses `ISNULL` sentinels (-1, -2); two practically impossible cases are missed.
- Run the full e2e suite once after F1 and R04 merge (it runs only manually and weekly); watch `12-low-stock` ("New receipt line never appeared" once).
- Deferred: backend text limits (partner 120, product 80); `includeTotal=false` once a screen shows no total; Dependabot retries for stylelint 17 (needs `stylelint-config-standard-scss` support) and the .NET group (Microsoft.CodeAnalysis 5.9.0 conflict).
- Owner spot-check: on a phone, tap a quantity field on a receipt and type; the value should be replaced (R27, untested on real iOS and Android).

## Review follow-ups, round 2 (minor, none blocking; found 2026-10-12)
- #60 nits: line amounts of about 90 trillion and above lose cent precision in a double (the API allows 1,000 trillion); `lineAmount` throws on NaN/Infinity if called directly. Chrome rewrites pasted "1,5" to 15 before the badInput check runs (a paste handler is a follow-up); the footer total skips blank-cost lines, so "Items (n)" and "n lines" can disagree.
- #63 minors (RP00): four components still call `has()` for cost/valuation; Home `$canApprove` and the document editor test only `document.approve` (the Owner loses Home's approvals card once the API gives the Owner only `document.approve.large`: do it in RP01); `ApprovalsStore` still calls `SystemSettingsStore.ensureLoaded()` for nothing; the role-name lint guard misses template literals and concatenation; no test for the approvals route guard accepting the large tier.
- #65 minors (R08): the Saved state widens short labels (Save 67 -> 105 px) and has no `aria-disabled`; the zh/ms "Saved" strings need native review.
- #68 minors (R10): Chrome leaves a control that is already in view but 17-26 px under the sticky action bar (Remark box); the bar-height watcher needs `ResizeObserver`; the keyboard-aware tab bar is not built (iOS Safari untested, needs a real-iPhone QA round); the tabpanel wraps only the table, not the filters.
- GUID plan: warehouse-id probing on import (two messages: "no access" vs "unknown", by warehouse code) is not fixed by GUIDs; a deactivated admin's access token works until it expires (pre-existing); the documents list INNER JOINs `[User]` through the soft-delete filter; k6 scenarios and `e2e/src/api.ts` still assume numeric ids (PR-2b); the audit history of flipped entities mixes integer and GUID ids; add the general "every id is uuid" OpenAPI test (PR-10); optional `Database:CommandTimeoutSeconds` setting for very large migrations; run `docker compose up` once before the first deploy (key ring volume, never tested).
- #105 follow-ups (security review): purge `RefreshToken` rows that are revoked or expired for more than N days (every rotation adds a row, and the 300-per-minute refresh ceiling lets one session add them about 15 times faster than before); key the rate limits on the IPv6 /64 prefix instead of the full address (`Caller()` in `PresentationServices.cs`; a client holding a /64 can rotate addresses to get fresh sign-in and refresh buckets).
 (or provide them per page), with a spec per store. Also from #120: "My drafts" opens the list of the first draft's type, and the 25-row feed is now skipped when nothing is returned, so with no returned draft the tile always opens Receipts until the filters-in-URL slice (A07) lands.

## Open questions for the user (from PRD 14.3 and delivery)
Defaults and deadlines for all owner decisions: [roadmap section 8.2](docs/pm/ROADMAP.md). Still open: O1 corrections confirmations (by WD 5), O3 hosting direction (by WD 10), O4 `large-change` label for the id-switch merge (by WD 15), O10 native speakers for zh and ms. Closed by the lean approval: roles defaults, tables, motion, date field and `Partner.Code` (not now).
- Hosting model for deploy.yml: customer-owned servers, our cloud, or hybrid? Recommended default: one small VM (about US$6-24 a month) with Docker Compose; free-tier only for a data-free demo; managed always-on cloud is about US$100-200 a month
- Default negative-stock policy and approval thresholds (currently: off; issues of 5,000 or more need a Supervisor).
