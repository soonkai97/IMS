# IMS — Task Breakdown (from PRD v0.2)

Stack: Angular 21 (PWA) · .NET 10 (Clean Architecture, CQRS) · SQL Server.
Scope of this pass: **Phase 0 (Foundation) + Phase 1 (MVP)**. Phases 2–4 are backlog at the bottom.
Legend: [x] done and verified · [~] in progress · [ ] not started

## Phase 0 — Foundation
- [x] T01 Solution scaffold: Domain / Application / Infrastructure / Api / Tests
- [x] T02 EF Core + SQL Server, migrations (append-only triggers, READ_COMMITTED_SNAPSHOT), seed
- [x] T03 AUTH-01 Login, JWT + refresh-token rotation, 5-failure lockout
- [x] T04 AUTH-02 RBAC by feature + warehouse scope (403 on API)
- [x] T05 AUTH-03 Immutable audit log (row-level + business events)
- [x] T06 OpenAPI document + Swagger UI (`/swagger`)
- [~] T07 Angular 21 PWA scaffold, Nx 4-tier layout (four tiers: shared, core, features, containers), `app-*` wrappers, auth, i18n (en/zh/ms)

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
- [~] T18 Tests: domain unit + architecture (agent running), API integration on real SQL Server (agent running); smoke test 41/41 green
- [~] T19 Frontend screens (agent running)
- [ ] T20 End-to-end verification with UI (behaviour tests, QA personas)

## Quality
- [x] Q1 Architecture: Clean Architecture, CQRS (commands/queries + dispatcher + pipeline), vertical slices, one type per file
- [x] Q2 Rich domain model (behaviour, factories, business rules, domain services)
- [x] Q3 Constants and messages centralised, no magic numbers/strings
- [x] Q4 Logging (Serilog, correlation id), health checks, ProblemDetails
- [x] Q5 Deadlock policy (ordered locks, optimistic concurrency, retry on 1205, RCSI) — proven by 24-way parallel postings
- [~] Q6 Independent architecture review (agent running) → docs/reviews/backend-initial/
- [~] Q7 DBA performance check at 100k ledger rows (agent running) → docs/dba/

- [x] Q8 Standard audit columns on every table (REC_DELETED, REC_CREATOR, REC_CREDATE, REC_REVISOR, REC_REVDATE, ROW_VERSION), soft delete, filtered unique indexes
- [x] Q9 Physical file storage for images/documents (attachments), type by content, owner policies
- [x] Q10 Server-side paging everywhere, seek paging for large lists, OFFSET depth cap
- [x] Q11 Security review fixes (lockout concurrency, change password, import zip-bomb check, PDF active content, release gated on CI, demo seeding local only)
- [x] Q12 Git workflow guards: `.githooks` (pre-commit, commit-msg, pre-push) + `scripts/guard/*`, `pr-guard.yml`, `.github/rulesets/main.json`, `docs/team/GIT-WORKFLOW.md`. Setup per clone: `node scripts/setup-git-hooks.mjs`

## Delivery
- [~] T21 CI/CD written (+ `pr-guard.yml`, `codeql.yml`, npm audit, web job on Nx scripts): `ci.yml` (build, unit, architecture, integration on SQL container, smoke, web, audit), `release.yml` (image to GHCR), `deploy.yml` (gated skeleton; hosting target undecided, PRD 14.3)
- [ ] T22 Push to https://github.com/soonkai97/IMS (private). Waiting for the test agents to finish so the first CI run is green; then first push needs the user's browser sign-in for Git Credential Manager
- [~] T23 Reusable base: rename script + `docs/TEMPLATE.md` done; framework extraction (Framework.* packages, npm lib) after a second project exists

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
- Hosting model for deploy.yml: customer-owned servers, our cloud, or hybrid?
- Default negative-stock policy and approval thresholds (currently: off; issues of 5,000 or more need a Supervisor).
