# IMS — Task Breakdown (from PRD v0.2)

Stack: Angular 22 (PWA) · .NET 10 (Clean Architecture, CQRS) · SQL Server.
Scope of this pass: **Phase 0 (Foundation) + Phase 1 (MVP)**. Phases 2–4 are backlog at the bottom.
Legend: [x] done and verified · [~] in progress · [ ] not started

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

## Roadmap epics (order, estimates, migration and client rules: [docs/pm/ROADMAP.md](docs/pm/ROADMAP.md))
Working days (WD) count from Monday 12 Oct 2026 and include a 25% rework allowance; total about 72 WD (14 weeks), range 55-96 WD. Phases P0-P6 and the milestones M1-M7 are in the roadmap, sections 0 and 6.
- [~] EP01 Money and QA backend fixes (P0, M1, ends WD 3): PR #44 average cost on reversal (changes required, being fixed), #45 money with exactly 2 decimals (being fixed), replacement for #46 (search, one low-stock rule, ledger balance). Merge in that order; #46's migration is re-created after #44 (migrations MG1, MG2). About 16 agent-hours left
- [ ] EP02 Backend hardening and limits (P1-P3): ER-06, ER-07, ER-08, ER-14, ER-21, B20 text limits (owner question P12), login/refresh limiter split (SEC-08), then B1-B3 after the id switch, B18 unlock with the Users screen. About 1,750 lines / 27 h
- [ ] EP03 Document corrections: recall, send back, cancel on Approved (closes NR-02, PR-02). DC-BE1, DC-BE2, DC-FE1, DC-FE2, DC-E2E; no migration expected. [design](docs/design/document-corrections.md). About 3,150 lines / 30 h; needs 3 owner confirmations (defaults exist). R28 and B19 of the refresh plan are superseded
- [ ] EP04 Public GUID ids, hybrid (P1-P2, M3): ADR accepted. PR-0..PR-3 on main (migrations MG3 `AddPublicIds`, MG4 `AddPartnerCode`), PR-4..PR-8 on `feature/public-ids` (no migration there), PR-9 one merge with the `large-change` label, PR-10 verification. [ADR](docs/architecture/adr-guid-keys.md). About 6,250 lines / 85 h
- [~] EP05 UI refresh, 73 slices R/A/Q/L (P1-P5, M2, M4, M6): R01 and R02 being built. Slices that carry ids (R17-R20, A07, A09, A11, A16, A18, Q06) wait for the id switch; R27 replaces R17 in the early waves; R28 dropped. [plan v2](docs/design/2026-refresh-plan.md), [sign-in](docs/design/2026-refresh-login.md). 49,260 lines / about 365 h
- [ ] EP06 Configurable roles and permissions, backend RP-0..RP-8 (RP-9 optional): RP-0..RP-2 may merge before the id switch (migration MG5 `AddRolesAndPermissions`), RP-3 onward only after PR-9. [ADR](docs/architecture/adr-roles-permissions.md) is Proposed; needs owner answers. About 10,200 lines / 137 h + 30 h review and QA
- [ ] EP07 Roles and permissions screens RP01..RP10 plus a small profile helper built early (P3-P4, M5): needs RP-5..RP-7, R14, R26, A16. [UX](docs/design/roles-permissions-ux.md). About 9,500 lines / 80 h
- [ ] EP08 Motion language: [spec](docs/design/2026-refresh-motion.md) landed 2026-10-11: new slices M01-M08 (M08 optional) plus growth of 20 slices, 6,680 lines / about 56 h, about +6 WD. Owner answers M1-M8 before R08 (about WD 4). Plan v2 not yet updated (fe-architect applies its section 7). With it the refresh is 81 slices / 55,940 lines
- [ ] EP09 Deployment and security follow-ups (P6, M7): hosting decision (owner), `deploy.yml`, Q14 (web on the refresh cookie, headers, AV hook, DAST triage). Demo environment after M3. 24-40 h depending on the hosting route
- [ ] EP10 Release gate (every milestone): five QA rounds (three personas, one at a time, about 2.5 h each), gates G1-G7, native review of zh and ms, device checklist, R24 after soak. Folds in T20

Milestones: [ ] M1 money and QA backend fixes merged (WD 3) · [ ] M2 sign-in page and new palette visible (WD 10) · [ ] M3 public ids and the correction workflow (WD 24) · [ ] M4 advanced table and Home screens (WD 38) · [ ] M5 configurable roles (WD 50) · [ ] M6 full 2026 refresh and the quality gate (WD 68) · [ ] M7 deployment (WD 72). Acceptance checks: roadmap section 6.

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

## Open questions for the user (from PRD 14.3 and delivery)
Defaults and deadlines for all owner decisions: [roadmap section 8.2](docs/pm/ROADMAP.md). Answer first (about 30 minutes): O1 corrections confirmations, O2 roles ADR answers, O3 hosting direction, O7 motion questions M1-M8.
- Hosting model for deploy.yml: customer-owned servers, our cloud, or hybrid? Recommended default: one small VM (about US$6-24 a month) with Docker Compose; free-tier only for a data-free demo; managed always-on cloud is about US$100-200 a month
- Default negative-stock policy and approval thresholds (currently: off; issues of 5,000 or more need a Supervisor).
