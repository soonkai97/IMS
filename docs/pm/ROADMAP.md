# IMS delivery roadmap (version 2, lean)

Owner: project-manager. Re-baselined 2026-10-12 after the product owner approved `docs/design/lean-scope.md` ("no over-engineering"). Version 1 (98-slice plan, 72 working days, 915 agent-hours) is in git history (`git log -- docs/pm/ROADMAP.md`); do not plan from it.
Clock: WD n = the n-th working day, WD 1 = Monday 12 October 2026, five working days a week, no holidays or leave counted (the year-end break adds one to two weeks to anything after WD 50).
Scope rule: **`docs/design/lean-scope.md` is the scope.** Slice contents, sizes, deferred triggers (its section 4), dropped items (section 5) and simplified designs (section 6) are there and are not copied here. This file holds the schedule, the migration and client-batch rules, risks and decisions.
Refreshed 2026-10-19 (WD 6) against `origin/main` at `7fdbbc8` (#133), `gh pr list` (open: #130, #134, #135) and `docs/team/HANDOFFS.md`. #44, #45 and #51 are merged and #46 is closed (superseded). Nothing was rebuilt for this document; a statement marked "unverified" was not checked against code or CI. Text below that describes the 12 October state and is kept as history says so.

## 0. The first page

**Totals.** 32 frontend slices, about 17,600 changed lines, in 16 waves (at most 3 builds each, usually 2); backend track of section 3.4; about 415 agent-hours (about 520 with the 25% rework allowance); about 38 working days (range 30 to 50), i.e. M6 on about Wed 2 Dec 2026. Against version 1: slices -67%, lines -74%, hours -55%, calendar -47%.

**Done (2026-10-19).** The MVP tasks T01 to T19, T21, T22 are built; T20 is folded into the QA rounds. Of the lean plan: EP01 (#44, #45, #51, #52), EP03 corrections (backend #85, web #92), EP04 public ids (PR-1..PR-3 on `main`, PR-4..PR-9 as #78, retests #79, #81, #82) and EP06 roles backend (RP-0..RP-6, RP-8: #84, #89, #90, #102, #104, #106, #118, #119, #124, #126) are merged. Web: waves 1 to 3, 7 to 9 and 13 are merged; wave 4 except Q1; wave 5 except L02; wave 6 (R13 #125, TB #121, R25 #88); wave 10 except R14+R15; wave 11 has HOME (#120); wave 12 has RP01 (#123); RP02a, b, c (#127, #131, #133). The schedule is ahead of the baseline (RP01 and RP02, planned after M4 at WD 25, are merged on WD 6); the WD 10 re-estimate decides the new dates.

**Left, in order.**
1. Review and merge the QA-round fixes in flight: #134 (Users custom roles, no-warehouse warning), #135 (no API data in the service worker, keep work on session expiry), and the phone-flow PR (not open when this was written). #130 (ADR, modules and tracking dimensions) merged and is Accepted (2026-10-19); the pilot slices PL1 to PL4 that follow from it are EP11 below, a separate path beside the lean refresh (plan: `docs/pm/pilot-slice-1-plan.md`).
2. Web slices not started: L02 and Q1 (they block M2), R14+R15 (blocks M3), A07, R26 (partly met by #135), R29+R30 (M4), then the P04 web part, R24 after one release of soak.
3. Backend: EP02 hardening (not started, 12 h), the Standard-approval guidance hint (decided 2026-10-19), then the QA findings list of `TASKS.md`. EP09 waits for the hosting decision.

**Owner answers still needed** (section 8.2): hosting direction (O3, by WD 10, Fri 23 Oct), native speakers for zh and ms (O10), and whether the e2e smoke set becomes a required PR check (`docs/team/E2E-HEALTH.md`). Taken: the corrections confirmations (O1, defaults built in #85), the `large-change` label (O4, applied to #78), the lean-scope approval (roles first with exceptions deferred, the lean table and motion sets, `Partner.Code` "not now", roles defaults O2), and on 2026-10-19 the Standard approval rule (section 8.1, item 17).

**Three rules that keep this from colliding** (details in section 3):
- One database migration open at a time, always created after the previous one is on `main`; the id-switch integration branch contains no migration.
- The generated web client is regenerated only in named batches (CS1, CS2, CS3, CS5), by one agent, never in parallel with another regeneration.
- At most 4 agents at once and at most 2 of them heavy (API plus SQL, integration suite, DBA measurement, a QA persona on a real API).

## 1. Dependency graph in words

EP01 (money and QA backend) lands first because it changes the schema and the DTOs everything after builds on. The corrections backend (B21, B22) goes next on `main`, before the id flip begins and before the roles oracle fixtures are captured (both rewrite `DocumentActionPolicy` and `StockDocument`).

The id switch (EP04) is the hinge. Its schema half (`AddPublicIds`, PR-3) is additive and goes to `main`; its wire half (PR-4 to PR-7, then the web G01) lives on `feature/public-ids` and reaches `main` in one step (PR-9). HOME, A07 and the roles screens wait for G02; the roles backend RP-3 onward waits for PR-9. Everything else in the refresh (palette, tokens, controls, shell, table, overlays, sign-in page, layout) is independent of ids and runs beside the backend flip.

```
(12 October picture; status in sections 0 and 3.1)
main + #44 -> #45 -> #51 (EP01)
   |
   +-> R02/R01 (merged) -> R27, R03, R33 -> R04, F1, RP00 -> R08, R10, Q1 -> R11, L02, A02 -> R13, TB, R25   (M1, M2)
   +-> B21, B22 lean (corrections) -> R28 -> R31                                                         (M3)
   +-> GUID PR-0 -> PR-1 -> PR-2/2b -> PR-3 [migration on main]
   |        \-> cut feature/public-ids -> PR-4..PR-7 -> G01 (web, CS3) -> PR-9 (merge) -> G02 -> PR-10      (M3)
   +-> RP-0 -> RP-1 -> RP-2 [migration on main, after PR-3]; after PR-9: RP-3 -> RP-4a/RP-5 -> RP-4b/RP-6 -> RP-8
   +-> after G02: R14+R15, HOME, A07, R26, R29+R30; roles screens RP01 -> RP02 -> RP03 -> RP05+RP11       (M4, M5)
   +-> R24 clean-up after one release of soak                                                              (M6)
```

## 2. Epics (lean)

Hours include build, fix and the independent review. "Plan" = version 1 of this file. Details: `lean-scope.md` section 2.7 and 6.4. Statuses: `done`, `in progress`, `not started`, `deferred`, `dropped`.

| ID | Epic | Lean content | Plan h | Lean h | Owner role | Status |
|---|---|---|---|---|---|---|
| EP01 | Money and QA backend (#44, #45, #51) | Finish as planned (B4, B5, B6 close here) | 16 | 16 | be-framework, be-dba, integration-test-engineer | done (#44, #45, #51, #52) |
| EP02 | Backend hardening | Bug and security items only: ER-06, ER-07, ER-08, ER-14, ER-21, SEC-08. B1-B3, B18, B20 deferred | 27 | 12 | be-framework, be-architect | not started (partly deferred; unverified: no merged commit names its items) |
| EP03 | Document corrections | Recall (Edit button) and cancel on Approved with a reason; send back deferred. [design](../design/document-corrections.md), lean-scope 6.3 | 30 | 20 | be-framework, fe-framework | done (B21, B22 #85; R28, R31 #92) |
| EP04 | Public GUID ids (hybrid) | Trimmed chain PR-0..PR-10 (`lean-scope.md` 6.2); includes G01, G02; `Partner.Code` and MG4 deferred | 85 | 74 | be-framework, be-dba, fe-framework | done (PR-9 #78; retests #79, #81) |
| EP05 | UI refresh, lean | 25 slices: R 18 (incl. HOME), F1, L 2, Q1, A 3 (lean-scope 3.1). The lean motion set is inside these numbers | 365 | 90 (EP05 + EP08) | fe-framework, behavior-test-engineer | in progress (open: L02, Q1, R14+R15, A07, R26, R29+R30, R24) |
| EP06 | Roles backend | RP-0..RP-6, RP-8 simplified (lean-scope 6.1); RP-7 overrides deferred, RP-9 dropped | 167 | 120 | be-framework, be-dba, security-engineer | done (RP-0..RP-6, RP-8; follow-ups in TASKS.md) |
| EP07 | Roles screens | RP00, RP01, RP02, RP03, RP05+RP11; RP04, RP06 to RP10 deferred or dropped | 80 | 22 | fe-framework | in progress (RP00, RP01, RP02a-c merged; #134 in PR) |
| EP08 | Motion language | Replaced by the lean motion set; M02, M04, M06, M07, M08 dropped | 56 | (in EP05) | fe-designer | replaced |
| EP09 | Deployment and security follow-ups (Q14) | Unchanged scope; waits for the hosting decision (O3) | 32 | 30 | security-engineer, orchestrator | decision open |
| EP10 | Release gate | 4 QA rounds, 3 gates; native review of zh and ms stays `TODO-review` until O10 | 60 | 30 | qa-* personas, fe-designer | in progress (a three-persona round ran 2026-10-19; gates open) |
| | **Total** | | **about 915** | **about 415 (520 with 25% rework)** | | |
| EP11 | Pilot slices PL1 to PL4 (small trading / wholesale; owner decisions 2026-10-19) | PL1 module foundation + tax-ready fields + accounting export (9 sub-slices), PL2 purchase orders, PL3 sales orders and returns, PL4 stocktake. Not part of the 415 h above. [plan](pilot-slice-1-plan.md), [ADR](../architecture/adr-modules-and-tracking-dimensions.md) | - | about 54 agent-days with rework (PL1 14.5; PL2 to PL4 are bands) | be-framework, be-dba, fe-framework, security-engineer | not started (waits for owner answers, plan section 11) |

Deferred and dropped items are listed with their triggers in `lean-scope.md` sections 4 and 5. A deferred slice is built only after the project-manager records its trigger in `docs/team/HANDOFFS.md`; a new request enters through the project-manager with a stated requirement (the checklist, section 8.1 item 14).

## 3. The sequenced plan

### 3.1 Waves

Slots: BE-1 (id and schema lane), BE-2 (feature lane), WEB-1, WEB-2, plus floating review, QA and PM runs. At most 3 web builds per wave; the roadmap runs 2 while both backend slots are busy. Slice contents and sizes: `lean-scope.md` section 3.1 (lane letters F, O, Y, T, S, Q, V, U, E, G, H, X).

| Wave | Builds (lines) | Gate | Status |
|---|---|---|---|
| 1 | R02 (550) / L00 (500) / R01 (450) | merged, CI green | done (R01 #49, R02 #50, L00 #54) |
| 2 | R27 (450) / R03 (350) / R33 (200) | merged | done (R27 #53, R03 #55, R33 #57) |
| 3 | R04 (450) / F1 (650) / RP00 (300) | M1; QA round 1 (one persona, on a phone) | done (R04 #60, F1 #59, RP00 #63); M1 reached on content |
| 4 | R08 (700) / R10 (550) / Q1 (600) | merged | done except Q1 (R08 #65, R10 #68); Q1 not started |
| 5 | R11 (650) / L02 (600) / A02 (650) | merged | done except L02 (R11 #74, A02 #73); L02 not started |
| 6 | R13 (600) / TB (600) / R25 (750) | M2; QA round 2; designer checks motion on one real low-end Android phone | built (R13 #125, TB #121, R25 #88); gate open: QA round and the real low-end phone motion check |
| 7 | R28 (600) | merged (needs B22) | done (R28 #92) |
| 8 | R31 (450) | merged | done (R31 #92) |
| 9 | G01 (900), exclusive | lanes T and E frozen (about 1.5 days) | done (G01 inside #78) |
| 10 | G02 (300) / R14+R15 (750) | M3; IDOR retest by security; QA round 3 | G02 inside #78 and the IDOR retest (#79) done; R14+R15 not started, so M3 is open |
| 11 | HOME (450) / A07 (500) / R26 (400) | merged | HOME done (#120); A07 and R26 not started (#135 meets part of R26) |
| 12 | R29+R30 (750) / RP01 (450) | M4 | RP01 done (#123); R29+R30 not started (a phone-flow PR is being prepared) |
| 13 | RP02 (550) | merged | done (RP02a #127, RP02b #131, RP02c #133) |
| 14 | RP03 (1,000) | merged | content built inside RP02b and RP02c; whether a slice remains is unverified |
| 15 | RP05+RP11 (700) | M5; QA round 4 with the edge persona leading | RP11 and the warehouse switch done in #108; custom-role select #134 in PR; QA round 4 open |
| 16 | R24 (200) | M6; final pass; one release of soak before this wave | not started |

Waves 7, 8 and 13 to 15 are narrow on purpose: they follow backend readiness (corrections, ids, roles). The orchestrator fills a spare slot with fixes found by QA.

Statuses of the table above refreshed 2026-10-19 from `git log origin/main` and the HANDOFFS entries; a QA round is "open" when no report newer than 2026-10-11 is in `docs/qa/` (the 2026-10-19 round is described in `TASKS.md`).

### 3.2 One linear migration order

Chain on `main` (verified 2026-10-19, 18 migrations): `Initial` (20261009135544) through `DropLegacyRoleColumns` (20261017125040); the table shows the ones this plan owns. A new migration is created only by the holder of the baton (one open PR with a migration at a time), after rebasing on `main`, by the agent who writes the entity change; be-dba reviews every one (Up, Down, up-down-up, data).

| # | Migration | Epic and PR | When | Notes |
|---|---|---|---|---|
| MG1 | `AverageCostMarkersAndCostScale` (20261011030307) | EP01 #44 | merged (#44) | Money precision and the average-cost markers. |
| MG2 | As built, three migrations: `StoredLineAmount` (20261011063707, #45), `AddDocumentReversedBy` (20261011072009, #51, replacement for the old `DocumentSearchIndexAndReversedBy`) and `AddLedgerBalanceAfter` (20261011133108, #52) | EP01 | merged in that order | The old timestamp (20261011021255) is older than MG1's. After #44 merges: `dotnet ef migrations remove`, rebase, `migrations add` again; never hand-merge the snapshot. |
| - | (no migration) corrections | EP03 B21, B22 lean | merged (no migration; #85) | The design says no new columns. If review finds one is needed, it takes this slot. |
| MG3 | `AddPublicIds` | EP04 PR-3 | merged (#64) | `PublicId` on 9 tables, unique unfiltered index, fill factor 90, backfill, `Down`. Inert until PR-4. |
| MG4 | `AddPartnerCode` | | **deferred** ("not now", lean-scope 4) | Built only when a customer needs partner codes. |
| MG5 | `AddRolesAndPermissions` | EP06 RP-2 | merged (#90) | `Role` is a public-id table from day one; `RolePermission`, `User.AllWarehouses`, approval-rule tier. Rights-parity test on a fresh and a migrated database. |
| MG5b | `DropLegacyRoleColumns` (20261017125040) | EP06 RP-4b | merged (#106) | Drops `User.Role` and `ApprovalRule.RoleRequired` after RP-4a stopped reading them; `Down` tested as a generated script as well as through the runner. Not in the 12 October plan. |
| MG6, MG7 | stock value snapshot, user preferences | | **dropped** | Charts and preference roaming are not built (lean-scope 5). |
| MG8 to MG10 | `GrantModulesManage` (data only), `GrantAccountingExport` (data only), `AddTaxReadyFields` (nullable `Product.HsCode`, `SstCode`, `Partner.Tin`, `SstRegNo`) | EP11 PL1.1, PL1.2, PL1.5 | after the migration chain on `main` (last: `DropLegacyRoleColumns`), one at a time, in this order | Each `migrations add` waits for the previous merge. PL2 to PL4 add their own later. |

Rules that make collisions impossible:
1. `feature/public-ids` contains no migration. All schema for the id switch lands on `main` first; PR-4 to PR-7 are code only. Reverting PR-9 needs no database change.
2. One migration PR open at a time. The orchestrator hands the baton in HANDOFFS ("baton: EP04 PR-3"); the next does not start `migrations add` until the previous one is merged.
3. Snapshot conflicts (`AppDbContextModelSnapshot.cs`, `*.Designer.cs`) are never merged by hand: remove your migration, rebase, add it again.
4. A test that fails when the model and the snapshot differ (`Database.HasPendingModelChanges()`) is required; it exists (`tests/Ims.Api.Tests/Tests/Schema/SchemaShapeTests.cs`).
5. Migration `Down` is tested; the 1M-tier copy is used for any migration that touches a large table (none above does).
6. A release that adds a permission ships a migration granting it to Admin; a test on a fresh database fails the build otherwise (lean-scope 6.1).

### 3.3 DTO and OpenAPI changes, and when the web client is regenerated

The client (`web/libs/shared/api`, `web/openapi`) is generated by `npm run api:generate` from a running API. Backend PRs never commit generated files. Until a batch regenerates it the web compiles against the previous client, because every change before CS3 is additive.

| Batch | Backend content on `main` | Web consumer | Regenerated by |
|---|---|---|---|
| CS1 (after #44, #45, #51) | ledger balance after, one low-stock rule, `reversedBy`, search, money rounding | R33 | fe-framework, in the R33 PR |
| CS2 (when B22 and the opaque cursors, PR-2, are on `main`) | recall endpoint, cancel body with reason, new `allowedActions` strings; opaque `cursor`/`nextCursor` | R28, R31 | fe-framework, first commit of R28's branch, merged alone |
| CS3 (inside G01, on the integration branch) | ids as uuid strings; `DocumentLineDto.id`, `LedgerRow.id`, `AuditLogDto.id` removed; problem-details `productId` as string | ids to string in about 100 files, routes, stores | fe-framework, once, in G01 only |
| CS4 | `returned` filter, ascending order, `updatedAt` (B1 to B3) | HOME variants | **deferred** with B1 to B3 |
| CS5 (when RP-5, RP-6 are on `main`) | roles, permissions, `PermissionCode`, `UserInfo` additive fields, approval rule tier | RP01 to RP05 | fe-framework |
| CS6 (after pilot PL1.1 and PL1.2) | `/api/modules`, `UserInfo.modules`, `module` on permissions | PL1.3, PL1.4 | fe-framework |
| CS7 (after pilot PL1.5 and PL1.6) | tax-ready fields on products and partners, accounting export endpoint | PL1.7, PL1.8 | fe-framework |

Status 2026-10-19: CS1 merged in R33 (#57); CS2 is in the client (recall is in `api-client.ts`; which PR carried it is unverified); CS3 merged inside G01 (#78); CS4 deferred; CS5 merged (#122). PR #134 regenerates the OpenAPI and the client itself (new `assignable-roles`, `roleName`, `roleRequired` removed), outside a named batch: a protocol exception to check at review.

Protocol for every batch:
1. Merge the batch's backend PRs first. The orchestrator writes "contract freeze CSn" in HANDOFFS.
2. One agent (fe-framework) regenerates against the merged API, fixes type errors only, updates typed stub fixtures, and opens a PR that contains nothing else. It merges alone.
3. Any other web PR open at that moment rebases after the merge; a PR that touches generated files itself is sent back.
4. Never two regeneration PRs open at once, and never a regeneration while the id-switch freeze is in force, except CS3.
5. Between batches the web does not consume a new backend field; if a slice needs one earlier, it uses the slice's stated fallback.

### 3.4 Backend track (runs beside the waves)

| Step | Content | Hours |
|---|---|---|
| P0 | Merge #44, #45, #51 (EP01); backend bug and security items of EP02 | 16 + 12 |
| Corrections | B21, B22 lean (recall, cancel on Approved), before the roles oracle fixtures | 12 |
| Ids | GUID PR-0 to PR-3 on `main`, PR-4 to PR-8 on `feature/public-ids`, PR-9 merge, PR-10 (IDOR retest plus one by-id timing check) | 74 |
| Roles on `main` early | RP-0, RP-1, RP-2 (no wire change) | 29 |
| Roles after PR-9 | RP-3, RP-4a, RP-4b, RP-5, RP-6, RP-8 | 70 |

Build order inside the ids step: SEC-11 (404 for out-of-scope ids) first; the flip before the roles endpoints are written so they use string ids from the first line.

Status 2026-10-19: P0 EP01 done (the EP02 items are not started); corrections done; ids done (PR-0 not found as its own commit, unverified); roles RP-0..RP-6 and RP-8 done. The remaining backend work is EP02, the QA findings of `TASKS.md` and the Standard-approval hint.

### 3.5 The integration branch for the id switch (finished: PR-9 merged as #78 on 2026-10-13; kept as the rule set for a future branch of this kind)

- **Cut.** `feature/public-ids` is created from `main` right after PR-3 (columns are on `main`, the API still speaks integers).
- **Stack.** PR-4 to PR-8 target the integration branch, not `main`. CI and the PR guard run on any pull request; each keeps its own review. Required checks apply only to `main`, so the merge captain (orchestrator) enforces "CI green plus review verdict recorded" by hand.
- **Sync.** The integration owner (be-framework) merges `main` into the branch before PR-4, before G01, before PR-9, and whenever an "id-sensitive" PR lands on `main` (anything under `src/Ims.Application`, `src/Ims.Api`, `tests/Ims.Api.Tests` builders, `web/libs/features/**/services`, `web/libs/containers/{documents,catalog,stock}`, `e2e/src`). The merge captain names these in HANDOFFS.
- **Allowed on `main` meanwhile:** web slices without ids, RP-0 to RP-2, hardening that touches no DTO, docs. **Not allowed until PR-9:** any endpoint, DTO, handler or policy change keyed by id (RP-3 onward) and any client regeneration.
- **Merge (PR-9).** One squash PR; it exceeds the 1,500-line guard, so the owner adds `large-change` (O4). First run the real-API e2e workflow by hand on the branch and the full integration suite.
- **Rollback.** Revert the merge commit; no database step. Web and API revert together.

## 4. What runs in parallel, and what cannot

Capacity: 3 to 4 agents at once, at most 2 heavy (anything starting the API with SQL Server, the integration suite, a DBA measurement, a QA persona or a real-API e2e). Web builds (lint, Jest, production build, stub Playwright) are light for the SQL limit.

| Slot | Normal use |
|---|---|
| BE-1 | id and schema lane: money PRs, GUID chain, RP-3 onward |
| BE-2 | feature lane: corrections, hardening, RP-0 to RP-2, RP-4, RP-5 |
| WEB-1, WEB-2 | the waves of section 3.1 |
| floating | independent reviewers (10 to 20 minutes per PR), PM, designers, security; QA personas (about 40 minutes each, one at a time) |

**Serial lanes:** migrations (one baton); contract (one regeneration batch at a time); merge train (one merge to `main` at a time, strict "up to date" policy re-runs CI); GUID chain PR-4, PR-5, PR-6, PR-7, then G01 and PR-9; RP chain; QA rounds (one persona at a time); web lanes of `lean-scope.md` 3.1 (nothing in a lane beside another member of it).
**Cannot:** GUID PR-1 before #44 and #51 are merged; RP-3 or RP-4a beside PR-5 or PR-7; the oracle fixtures (RP-0) before B22; two migration PRs; a web slice that builds ids before PR-9; HOME or A07 before G02.

Databases and ports (instance `ImsDev`; confirm against `e2e/README.md`): BE-1 `Ims_Dev` 5601; BE-2 `Ims_Verify` 5611; stub e2e none, web 5622; QA personas `Ims_QA` 5631/5632 (only they write it); real-API e2e and security retest `Ims_QA2` 5641/5642; DBA 1M tier `Ims_Perf` 5651 (exclusive). CI uses a fresh database per run.

## 5. Elapsed-time estimates

- A working day gives each slot about 6 productive agent-hours; with 4 slots and about 70% packing the team finishes about 17 agent-hours of work a day.
- Per PR: CI about 5 minutes twice (strict up-to-date), independent review 10 to 20 minutes, one in three needs a re-review. About 25 to 35 minutes of wall time per PR, mostly while the author is idle.
- Rework allowance: 25% on build effort (independent review, rebases, QA findings, migrations).
- 415 hours at about 17 hours a day gives 24 base days; web waves and serial backend chains (ids, roles) make the critical path about 30 days; with rework and four QA rounds: **about 38 working days (range 30 to 50, 6 to 10 weeks)**. Three days a week instead of five: multiply calendar figures by about 1.6.
- Optimistic (30 WD): rework 10%, the id switch lands first time, no gate finds a Major. Pessimistic (50 WD): rework 40%, the id switch needs a second attempt (a week), the motion check on the low-end phone sends R13 or R25 back.
- Note: R01 and R02 were merged before WD 1, so wave 1 starts with about 1,000 of the 17,600 lines done. The total is not reduced; the first re-estimate at WD 10 compares real throughput (about 5 agent-hours and 1.1 slices per lane per day) and decides.
- Deployment (EP09, 30 h) runs in a spare slot; it is not on the 38-day path. Target for M7 is about WD 40 if hosting is decided by WD 10 (PM estimate; moves with O3).

## 6. Milestones

| M | Name | After wave | Target (WD, date) | What the user sees | Acceptance check |
|---|---|---|---|---|---|
| M1 | Calm palette, money and quantity basics | 3 | WD 6, Mon 19 Oct | New palette in light and dark; red asterisk and "* means required"; `12,400.00` everywhere; typing in a quantity replaces the old value; one tap saves once; no page scrolls sideways; the Reports date error under the field; ledger shows balance after | #44, #45, #51 merged, CI green on `main`; each had its independent review with no open Blocker or Major; migrations apply to an empty database and a copy of `Ims_QA`, `Down` of the last two works; money endpoints show exactly 2 decimals half away from zero; CS1 merged; `npm run contrast` 0 failing; bundle under 600/650 kB; QA round 1 |
| M2 | New look and tables | 6 | WD 12, Tue 27 Oct | Buttons that press and show Saved; new top bar, tab bar, dialogs with a short pop or slide; route fade; tables with sticky header and first column, First/Last, page size, column show/hide; the new sign-in page; browser smoke with axe on every PR | Slices of waves 4 to 6 merged; Q1 green on every PR; sign-in acceptance (LG-*) and `25-login` pass; no horizontal scroll at 320 px and 400% zoom; reduced-motion check passes; QA round 2 with no open Blocker or Major |
| M3 | Corrections and public ids | 10 | WD 20, Fri 6 Nov | Edit on a wrong Submitted document (same number and lines); cancel on Approved with a reason; GUID urls and ids, 404 for out-of-scope ids; skeletons and Saved state on the main screens | Architecture test "no integer id on the wire" green; IDOR matrix green; cursors opaque; reconcile drill 0 differences; PR-9 merged with real-API e2e 01 to 13 green; corrections criteria pass in the new scenario file (NR-02, PR-02 closed); IDOR retest by security; QA round 3 |
| M4 | Home, filters, phone | 12 | WD 25, Fri 13 Nov | Home tiles open their list; returned drafts listed; filters survive the back button and can be shared; tidy phone document and list screens; session expiry no longer loses a form | Wave 11 and 12 slices merged; `13-cost-visibility` green and the DOM money scan clean on every new surface |
| M5 | Configurable roles | 15 | WD 33, Wed 25 Nov | An administrator adds a role (blank or copied), ticks permissions, assigns it to a person, sees the audit entry; the last administrator cannot be removed; a demotion takes effect within seconds; approval rules name a tier | Rights-parity test (oracle) green on a fresh and a migrated database; last-administrator race test and escalation matrix green; ER-15 and SEC-06 closed (freshness under 15 seconds); no role-name literal in `web/libs`; roles e2e green; QA round 4 (edge persona leading) |
| M6 | Lean refresh complete | 16 | WD 38, Wed 2 Dec | The compatibility layer is gone; final QA pass | R24 merged after one release of soak; 3 gates passed; full real-API e2e suite green; zh and ms either reviewed or marked `TODO-review` with the owner's consent |
| M7 | Deployment | spare slot | about WD 40 (PM estimate) | The released image runs on the chosen host and passes the smoke check | Hosting decided; `deploy.yml` deploys a `release.yml` image with a smoke check and tested rollback; web headers verified; `Auth__ReturnRefreshTokenInBody=false` after the web uses the cookie (Q14); backup and restore drill; demo seeding off; first DAST run triaged; no secrets in the repository |

**Status 2026-10-19 (WD 6).** M1: reached on content (EP01 merged, R01 to R04, R27, R33, F1; the QA round 1 report is not in `docs/qa/`, so the acceptance check "QA round 1" is unverified). M2: open because of Q1, L02 and the real low-end phone motion check; the rest of waves 4 to 6 is merged. M3: corrections and ids are merged and retested (#79, #81); open: R14+R15 (Saved state, skeletons) and the QA round 3 gate. M4: open: A07 (Home tiles open unfiltered lists), R26, R29+R30; HOME and RP01 are in. M5: open: #134, the P04 web part, QA round 4. M6, M7: not started; M7 waits for O3.

## 7. Risk register (top 10)

L = likelihood, I = impact (H/M/L). Ratings are those of 12 October and were not re-assessed on 2026-10-19 (risks 1 and 3 name events that have passed: #44/#46 and the PR-9 merge). Risks specific to going lean: `lean-scope.md` section 7 (not repeated).

| # | Risk | L / I | Mitigation | Owner |
|---|---|---|---|---|
| 1 | Migration collisions: #44 and the old #46 migration have out-of-order timestamps; the snapshot conflicts on every concurrent migration | H / H | The baton and the order in 3.2; the id-switch branch holds no migration; remove-and-re-add on conflict; pending-model-changes test; be-dba reviews all | orchestrator, be-dba |
| 2 | Generated web client churn | H / M | Named batches, one agent, nothing else in the PR, merged alone (3.3); typed stub fixtures | fe-framework |
| 3 | Merge-train mistakes: strict up-to-date policy, squash-only, a stacked PR accidentally targeting `main`, Dependabot PRs in the queue, the one over-limit PR-9 | M / H | One merge captain and a written merge order in HANDOFFS; stacked PRs target the integration branch; Dependabot majors (eslint 10, stylelint 17, lint-staged 17, jsonc-eslint-parser 3) wait until after M2; `large-change` decided in advance; revert plan (3.5) | orchestrator |
| 4 | Shared SQL and ports: agents overwrite each other's data | H / M | Assignment table in section 4; QA database written only by QA personas; each agent states database and port in its HANDOFFS entry | orchestrator |
| 5 | Scope creep returns: a deferred slice is built because its spec is still on disk; QA findings turned into slices; new owner ideas | H / H | The scope is `lean-scope.md`; the review checklist "which stated requirement needs this?" on every PR; a deferred item is built only after its trigger is recorded by the project-manager; QA findings go to the next wave's backlog, not into a running slice; re-baseline at WD 10, WD 20 and WD 33 | project-manager, every reviewer |
| 6 | Reviewer false negatives: reviewers and authors are the same kind of agent | M / H | Specialist reviewers by topic (money, auth, migrations, CI); the oracle parity test for roles; the IDOR retest for ids; "would the test fail if the code were wrong" is a review question; the project-manager spot-reads one merged PR in five | orchestrator, project-manager |
| 7 | Token and context limits of long agent runs | H / M | One slice per agent run; the orchestrator passes only the slice block, collision rows and the lean definition of done (`lean-scope.md` 3.5); stop and split at 1,000 lines; hand-offs written before a run ends | orchestrator |
| 8 | Node 22.6 (or 20.x) on the dev machine versus the required 22.22.3+: local and CI results differ | H / M | Install Node 22.22.3 or newer (about 30 minutes) and add an engines check; until then CI is the judge and agents state the flags they used | orchestrator |
| 9 | Public repository data hygiene (QA data, demo users, screenshots in CI artifacts, perf seeds) | M / H | `docs/private/` is git-ignored; the guard blocks backups and secrets; Q1 screenshots are CI artifacts from demo data only (no baseline branch); reviewers check docs for real names | security-engineer |
| 10 | The 25-slice refresh estimate is optimistic: the 135 lines per agent-hour rate is not yet measured on this team, and the 38 days include a blocked wait for the backend (waves 7 to 15 follow corrections, ids and roles readiness) | M / M | Re-estimate at WD 10 with real throughput; backend track starts now (section 0); a spare web slot takes QA fixes; the range is 30 to 50 days, not a promise | project-manager |

## 8. Decisions

### 8.1 Already taken (do not reopen)

1. **Lean scope approved (2026-10-12).** `docs/design/lean-scope.md` is the scope: 32 frontend slices, the lean backend track, deferred items with triggers, dropped items. The product owner's instruction: no over-engineering.
2. Custom `app-*` components, no UI library and no bake-off. No `@angular/cdk`, `jest-axe`, `@lhci/cli` or other new dependency without a stated requirement; `@angular/animations` is removed (F1).
3. Public ids: the hybrid. Integer clustered keys stay; random version-4 `PublicId` on the 9 tables; 404 (never 403) for ids outside the caller's scope; opaque encrypted cursors; no row id on ledger and audit responses. D1 = no offline creation, D2 = yes non-guessable; idempotent create (PR-7b) dropped.
4. No GUID primary keys on the ledger, audit, lines or balances, in any form.
5. Money: exactly 2 decimals on screen and in the API, rounded half away from zero, "RM" only in labels; average cost per base unit with up to 4 decimals; quantities up to 4 decimals.
6. Cost visibility by permission, not by role: Staff never see cost or value; Clerk sees cost but not valuation; configurable later through roles, with audit.
7. Roles first; per-user exceptions deferred (a copied role covers the odd person). Roles defaults stand: one role per person, two approval tiers, a role in use cannot be retired, cost visibility changeable by administrators with a reason and an audit entry.
8. "More advanced tables" is met by: sticky header and first column, cards on narrow screens, page size with First/Last, show/hide columns, filters in the URL. Resize, reorder, saved views, bulk actions and endless scrolling are deferred.
9. The lean motion set (`lean-scope.md` 2.5) is enough "animation". Only the OS "reduce motion" setting is honoured; no in-app switch.
10. Not now: `Partner.Code` with migration MG4. Triggers in `lean-scope.md` section 4. (The R23 date field was built after all, in PRs #96, #98 and #100, as `dd/MM/yyyy`.)
11. Document corrections: recall plus cancel on Approved; send back deferred.
12. The independent review gate before merge (`docs/team/PR-REVIEW.md` section 8) for every PR over about 200 lines and for any PR touching money, permissions, authentication, migrations, posting or CI/CD.
13. Taste questions (look, wording, motion details) are decided by the designers, not the owner. The repository is public and `main` is protected by a ruleset (PR required, four required checks, strict up-to-date, squash only, linear history). No bulk state-changing actions (N8); commands are never queued offline (N9); no drafts in browser storage.
14. **Review checklist for every slice and PR:** "Which stated requirement needs this? Is there a simpler way? Does it add a dependency or a configuration surface?" Added to `docs/team/PR-REVIEW.md` section 8 as "flag over-engineering: unneeded abstraction, speculative generality, configuration nobody will change, a new dependency without a stated requirement".
15. **Corrections confirmations taken (O1).** The defaults were built in B22 (#85): only the approver undoes an approval; the Owner and an Administrator may cancel a Large approved document; a reason of 5 or more characters cancels an Approved document.
16. **The `large-change` label (O4).** Applied to the id-switch merge #78, merged 2026-10-13.
17. **Standard approval (decided 2026-10-19).** A Standard-tier document is approved by a Supervisor; there is no Owner fallback. When a submitter cannot approve their own document the screen says "another Supervisor or an Administrator can approve this" (to build; `TASKS.md`, QA round findings).

### 8.2 Open owner decisions, with my recommended default

| # | Decision | Recommended default | Needed by | Cost of waiting |
|---|---|---|---|---|
| O3 | Hosting direction | One small VM with Docker Compose and Caddy (about US$6 to 24 a month); free tier only for a data-free demo; managed cloud (about US$100 to 200 a month) when a customer needs availability guarantees | WD 10 for a demo environment; WD 30 for production | M7 slips; no other work waits |
| O10 | Native speakers for zh and ms review | Named before M6; until then `TODO-review` stays | WD 30 | M6 acceptance wording |
| O11 | Make the e2e smoke set (10 files, about 2.5 minutes) a required PR check and run the full suite nightly (`docs/team/E2E-HEALTH.md`) | Yes, the smoke set required; nightly full run with a notification | Before M2 | A browser regression can again pass a green CI (found in the #121 review) |

Closed by the lean approval: O2 roles defaults (taken), O5 text limits (B20 deferred), O6 charts and polish (dropped), O7 motion (lean set), O8 refresh questions P1 to P11 (moot for dropped slices; plan defaults for kept ones), O9 `Partner.Code` (not now).

### 8.3 Conflicts

The version 1 conflicts C1 to C15 are closed by the lean scope or recorded in earlier HANDOFFS entries. Two remain visible: (a) `docs/design/2026-refresh-plan.md` still lists 98 slices; it now carries a banner pointing to `lean-scope.md` (it stays as the full reference); (b) `docs/design/roles-permissions-ux.md`, `adr-roles-permissions.md` and `2026-refresh-motion.md` describe the full designs; where they disagree with `lean-scope.md` (sections 2, 5, 6), the lean scope wins until the owner reopens an item.

## 9. How this file is kept true

- The project-manager updates `TASKS.md` and the wave table after every merge or gate, and re-estimates at WD 10, WD 20 (M3) and WD 33 (M5) using real throughput.
- The orchestrator writes the baton, the contract freezes and the id-sensitive merges in `docs/team/HANDOFFS.md`; this file only defines the rules.
- A change in scope, a finished epic, a fired deferred trigger or a decision from section 8.2 changes this file in the same working day. Open items older than their "needed by" day are reported as blockers.
