# IMS delivery roadmap (version 1)

Owner: project-manager. Date: 2026-10-11. Audience: the orchestrator and the product owner.
Clock: WD n = the n-th working day, WD 1 = Monday 12 October 2026, five working days a week, no public holidays or leave counted (the year-end break adds one to two weeks to anything after WD 50).
Placement note: the request was for `docs/ROADMAP.md`; the project-manager lane may edit only `TASKS.md` and `docs/pm/`, so the file lives here. `git mv docs/pm/ROADMAP.md docs/ROADMAP.md` is a one-line move if you prefer the top level (then fix the links in `TASKS.md`).
Checked by me (read only, nothing rebuilt): `gh pr list` (open: #44, #45, #46; merged: #47 docs), `git diff main...<branch>` for the three open branches (two new migrations exist, see 3.2), `.github/rulesets/main.json` and `scripts/guard/*` (strict "branch up to date" policy, squash only, 1,500-line PR limit with a `large-change` label), `.github/workflows/*`, and every document named in the request. The motion spec `docs/design/2026-refresh-motion.md` landed while this file was being written; it is included (EP08): 8 new slices M01 to M08 and growth in 20 existing slices, 6,680 lines. Not to be confused: milestones are M1 to M7 (no leading zero), motion slices are M01 to M08, migrations are MG1 to MG7.

## 0. The first page

**Where we are.** The MVP (`TASKS.md` T01 to T19, T21, T22) is built; T20 (end-to-end verification with the personas) stays open and is folded into the QA rounds below. In flight: PR #44 (average cost on reversal, review said CHANGES REQUIRED, being fixed), #45 (2-decimal money, being fixed), a replacement for #46 (search, one low-stock rule, ledger balance), and UI slices R01 and R02. Waiting for decisions: hosting (open), the roles ADR (Proposed), three small owner confirmations on document corrections, and the owner's look at the motion demo (questions M1 to M8 of the motion file). Accepted: the GUID ADR (hybrid, random public GUID on 9 tables).

**What happens next, in order.**
1. Merge #44, then #45, then #46, one at a time (each needs a fresh CI run after the one before it merges). #46's migration must be re-created after #44 merges, because its timestamp (`20261011021255`) is older than #44's (`20261011030307`). Web lane: R01, then R02, then CS1 (client regeneration) once #46 is in.
2. Owner answers four things in about 30 minutes (section 8, "answer first"): corrections confirmations, roles ADR defaults, hosting direction, and the motion questions (open `docs/design/assets/motion-demo.html`, accept the defaults or change them) before R08 starts at about WD 5. Nothing else is blocked on the owner for the next two weeks.
3. Second backend slot starts now on DC-BE1 (document corrections, domain only, no database) and GUID PR-0 (ADR bookkeeping). GUID PR-1 (the safety net that pins the schema) starts only after #44 and #46 are merged, otherwise it would pin the wrong schema.

**The seven phases (typical working days, range in brackets; includes the 25% rework allowance, section 5).**

| Phase | What | Ends at | Length | Milestone |
|---|---|---|---|---|
| P0 | Land the in-flight work | WD 3 | 3 (2-4) | M1 money and QA backend fixes merged |
| P1 | New look: palette to sign-in page | WD 10 | 7 (5-9) | M2 sign-in page and new palette visible |
| P2 | Document corrections and the public-id switch | WD 24 | 14 (11-19) | M3 public ids and the correction workflow |
| P3 | Advanced tables, Home screens, motion | WD 38 | 14 (11-18) | M4 advanced table and Home |
| P4 | Configurable roles and permissions | WD 50 | 12 (9-16) | M5 configurable roles |
| P5 | Finish the refresh, quality gate | WD 68 | 18 (14-24) | M6 full 2026 refresh |
| P6 | Deployment | WD 72 | 4 (3-6) | M7 deployed |

Total: about 72 working days (14 weeks), range 55 to 96 working days (11 to 19 weeks). Phases overlap (backend runs ahead of the web), so the ranges do not simply add up. Dates if work starts 12 Oct: M1 Wed 14 Oct, M2 Fri 23 Oct, M3 Thu 12 Nov, M4 Wed 2 Dec, M5 Fri 18 Dec, M6 Wed 13 Jan 2027, M7 Tue 19 Jan 2027 (the year-end break moves M6 and M7 by one to two weeks).

**Demo first:** M2 at the end of week 2 (new palette, required-field marker, `12,400.00` money, sign-in page, first motion on buttons and skeletons), opened by a 3-minute proof of M1 (reverse a receipt, Stock value comes back). Biggest value is M3 (week 5): the Staff "Edit" fix for a wrong quantity (NR-02) and non-guessable ids.

**Decide soon (defaults in section 8):** the motion questions (by WD 4), hosting (needed by WD 10 for a demo environment), roles answers (by WD 8), `large-change` label for the one big id-switch merge (by WD 22), whether charts and polish stay in (decide at WD 60, built last anyway).

**Three rules that keep this from colliding** (details in section 3):
- One database migration open at a time, always created after the previous one is on `main`; the id-switch integration branch contains no migration.
- The generated web client is regenerated only in five named batches, by one agent, never in parallel with another regeneration.
- At most 4 agents at once and at most 2 of them heavy (API plus SQL, integration suite, DBA measurement, a QA persona on a real API).

## 1. Dependency graph in words

Everything starts from `main` plus the three open PRs. The money and QA backend (EP01) must land first because it changes the schema (two migrations) and the DTOs that everything afterwards builds on.

The public-id work (EP04) is the hinge. Its schema half (`AddPublicIds`, `AddPartnerCode`, PR-3) is additive and goes straight to `main`. Its wire half (PR-4 to PR-7, then the web PR-8) lives on an integration branch and reaches `main` in one step (PR-9). Everything that builds, stores or compares an id must wait for PR-9 or be written against opaque strings: Home Doers R17, Home others R19, palette R18 and R20, filter URL state A07, bulk selection A09, virtual scroll A11, drawer A16, notifications A18, offline cache Q06, and the roles screens. Everything else in the refresh (palette, tokens, controls, tables up to A05, overlays, shell, sign-in page, layout system) is independent and runs while the backend flip is built.

Document corrections (EP03) is small and has no schema change: its backend (DC-BE1, DC-BE2) goes first, on `main`, before the id flip begins and before the roles oracle fixtures are captured (both rewrite `DocumentActionPolicy` and `StockDocument`). Its web part (DC-FE1, DC-FE2) needs the client regenerated once (CS2).

Roles (EP06 backend, EP07 web) split in two. RP-0, RP-1 (pure Domain, no wire change) and RP-2 (tables and migration, no wire change) can merge to `main` before the id switch. RP-3 onward touches handlers, DTOs, `UserInfo` and policies that the id switch also rewrites, so it merges to `main` only after PR-9. The roles screens need the catalog, role and person endpoints (RP-5 to RP-7), the refresh slices R14 (live regions), R26 (re-sign-in) and A16 (drawer), and the table lane up to A04.

The refresh (EP05) has one long chain, the table lane T (A02, A03, A04, L04, A05, A06, R15, A07, R23, A08, A09, A10, A11, R30: 14 slices, one after the other, then M06 and the optional M08). It is the critical path of the whole programme. The sign-in page (R25) needs R01, R02, R05, R06, R08, R11, A01.

The motion programme (EP08, slices M01 to M08) sits on top of the refresh: M01 needs R05 and R07, M02 needs M01, M03 to M06 need M02 plus their screens (R10, R17, R25, A03, A04, R14), M07 (test harness) needs Q01 and M01. R13 now needs M01 and R09 now needs M02, so M01 and M02 are early; they are not on the table lane except M06 and M08 at its end. Motion also changes slices already scheduled first (R05 +60 lines, R07 +100, R08 +150, R11 +80, A01 +100), so the owner's answer to the motion questions is needed before R08 starts.

Deployment (EP09) depends on nothing but the owner's decision; the pipeline half can be built in a spare slot as soon as the decision exists. It completes after M6.

```
main + #44 -> #45 -> #46 (EP01)
   |
   +-> R01 -> R02 -> R05 -> R04/R08 -> A01/R06 -> R11 -> R25                (M2)
   +-> DC-BE1 -> DC-BE2 (CS2) -> DC-FE1 -> DC-FE2 -> DC-E2E                 (EP03)
   +-> GUID PR-0 -> PR-1 -> PR-2/PR-2b -> PR-3 [migration on main]
   |        \-> cut feature/public-ids -> PR-4 -> PR-5 -> PR-6 -> PR-7 -> PR-8 (web, CS3) -> PR-9 (merge) -> PR-10   (M3)
   +-> RP-0 -> RP-1 -> RP-2 [migration on main, after PR-3]
   |        after PR-9: RP-3 -> RP-4a/RP-5 -> RP-4b/RP-6 -> RP-7 -> RP-8
   +-> lane T: A02 -> A03 -> A04 -> L04 -> A05 | after PR-9: A06 -> R15 -> A07 -> R23 -> A08 -> A09 -> A10   (M4 with Home R17, R19)
   +-> after PR-9, R14, R26, A16: RP01 -> RP02 -> RP03 -> RP04/RP05 -> RP07 -> RP06/RP09 -> RP08 -> RP10        (M5)
   +-> motion: M01 (after R07) -> M02 -> M03/M04/M05 (P3); M07 harness beside; M06 and M08 after A15/R30 (P5)
   +-> rest of the refresh, quality net (Q02, Q03, L08, Q11) early in P3/P4 -> G4..G7 -> R24                      (M6)
   +-> hosting decision -> pipeline -> demo environment -> production                                            (M7)
```

## 2. Epics

Sizes are changed lines (hand-written, tests included, generated code excluded) and agent-hours (build plus fix plus the independent review; ranges plus or minus 25%). Hours come from the source documents where they exist; where a row says "PM estimate" I derived it from those documents' rates (about 75 backend lines per agent-hour; a web slice of about 675 lines is about 5 agent-hours all in: build 4, fix round 0.5, review and CI 0.5).

| ID | Epic | Goal | Size (lines / agent-h) | Owner role | Needs first | Blocks | Status |
|---|---|---|---|---|---|---|---|
| EP01 | Money and QA backend fixes (#44, #45, #46) | Average cost correct after a reversal, money exactly 2 decimals, one low-stock rule, document search, ledger balance | about 8,200 in three PRs (git stat, with tests and generated files) / about 16 left | be-framework, be-dba (migrations), integration-test-engineer | nothing | everything that touches schema or DTOs | in progress |
| EP02 | Backend hardening and limits | ER-06, ER-07, ER-08, ER-14, ER-21, B20 text limits, login and refresh limiter split (SEC-08, S7), later B1, B2, B3 (Home filters), B18 unlock | about 1,750 / about 27 (PM estimate) | be-framework, be-architect | EP01 (part 1); PR-9 (B1 to B3 touch the documents spec) | Home R17 and R19 (B1, B2 fallbacks exist), R25 second lockout line (B18) | not started |
| EP03 | Document corrections (recall, send back, cancel on Approved) | Close NR-02 (Major) and PR-02 | about 3,150 / about 30 (PM estimate from the slice sizes) | be-framework, fe-framework, behavior-test-engineer | EP01; three owner confirmations (defaults exist) | RP-0 oracle capture, GUID PR-4 to PR-7 (shared files) | design decided, not started |
| EP04 | Public GUID ids (hybrid) | Non-guessable, non-countable ids on 9 tables, opaque cursors, ids as strings on the wire | about 6,250 / about 85 (ADR; 12 PRs, range 65-105) | be-framework, be-dba, integration-test-engineer, fe-framework, behavior-test-engineer | EP01 merged (PR-1 pins the schema); EP03 backend merged | H-marked refresh slices, roles endpoints, roles screens | ADR accepted, not started |
| EP05 | UI refresh, programmes R, A, Q, L (73 slices) | More advanced, more stable, more responsive UI, new look, sign-in page | 49,260 / about 365 (73 x 5 h all in; plan v2) | fe-framework, behavior-test-engineer, fe-architect (review) | R01, R02 in flight; EP04 for H slices; motion spec | M2, M4, M6, EP07 (R14, R26, A16, tables) | R01, R02 being built |
| EP06 | Configurable roles and permissions, backend (RP-0 to RP-8, RP-9 optional) | Roles in the database, permission catalog, exceptions, rights fresh within seconds (closes ER-15) | about 10,200 / 137 (ADR) plus about 30 review and QA | be-framework, be-dba, security-engineer | owner answers; RP-2 after GUID PR-3; RP-3 onward after PR-9; EP03 before the oracle | EP07 | ADR Proposed |
| EP07 | Roles and permissions screens (RP01 to RP10) | Roles list and editor, person's access page, exceptions, live change, compare | about 9,500 (9,200 plus a 300-line permission helper) / about 80 (PM estimate) | fe-framework, ui-ux-designer, behavior-test-engineer | RP-5 to RP-7 contract; R14, R26, A16, A02, A04, R13 | M5 | design done |
| EP08 | Motion language (slices M01 to M08 and growth of 20 slices) | Interface that feels alive and stays fast: tokens, primitives, route and theme transitions, Home and sign-in motion, a test harness with frame and INP gates | 6,680 (4,550 in new slices, 2,130 growth) / about 56 (PM estimate: 8 slices x 5 h plus growth at 135 lines per hour) | fe-framework, behavior-test-engineer (M07), fe-designer | owner answers M1 to M8; R05, R07 (M01); R10, R06 (M03); Q01 (M07) | R13 (needs M01), R09 (needs M02); acceptance MO-01 to MO-09 | spec landed 2026-10-11, plan not yet updated |
| EP09 | Deployment and security follow-ups (Q14) | Hosting decision, `deploy.yml` filled in, web moves to the refresh cookie, headers, AV hook, DAST triage | about 400 / 24 (one small VM) to 40 (managed cloud), PM estimate | security-engineer, behavior-test-engineer (workflows), orchestrator | owner decision on hosting; Q14 web step needs R26 | M7 | decision open |
| EP10 | Release gate | Five QA rounds, gates G1 to G7, native review of zh and ms, device checklist, soak, R24 clean-up | n/a / about 60 (5 rounds x 2.5 h plus about 48 h of fixes, PM estimate) | qa-novice, qa-power, qa-edge, fe-designer | each milestone | M6 | not started |

Totals: about 95,000 changed lines and about 915 agent-hours before rework (about 1,130 with the 25% allowance). The refresh (EP05 plus the motion programme) is about 46% of the hours; the two ADRs together (EP04, EP06, EP07) are about 36%.

### EP08: motion language, how it is scheduled

The motion file (fe-designer, 2026-10-11) turns plan v2 from 73 slices and 49,260 lines into 81 slices (80 without the optional M08) and 55,940 lines, and adds four waves. Its own estimate is about 6 working days on the plan's assumptions; this roadmap carries about 6 working days as well (P1 +1, P2 +1, P3 +3, P5 +1).

| Slice | Size | Where | Lane | Note |
|---|---|---|---|---|
| R05 +60, R07 +100, R08 +150, R11 +80, A01 +100 | +490 | P1 and P2 | Y, F, V | tokens, skeleton, buttons, toasts, popovers: they are built first, so the owner's answer is needed before R08 (about WD 5) |
| M01 Motion foundation (650) | 650 | P2, right after R07 | Y | before R13; removes `@angular/animations` |
| M07 Motion test harness (600) | 600 | P2, test lane, beside M01 | Q | frame, long-task and INP probes at 4x CPU throttle; MO-05 report-only until G3 |
| R10 +120, R13 +250 | +370 | P2 | S, V | R13 depends on M01 |
| M02 Motion primitives (900) | 900 | P3 | none | R09 (+200) waits for it, so R09 moves from P2 to P3 |
| M03 Route and theme transitions, top bar (650) | 650 | P3 | S | before L03 in the shell lane |
| M04 Home motion (400), M05 Sign-in motion (300) | 700 | P3, after R17 and R25 | H, U | disjoint lanes |
| R14 +80, R15 +150, A08 +120, A09 +40, A16 +120, A12 +40 | +550 | their own phases | various | |
| M06 Table, list and editor motion (700) | 700 | P5, after R30 and A15 | T, E | end of the critical path |
| A17 +40, A18 +60, A20 +200, A21 +100, Q10 +60, Q12 +60 | +520 | P5 | various | |
| M08 Pull to refresh (350, Could) | 350 | P5, last | T | first to cut |

Cut order if time is short (motion file 7.5): M08, the shared-element transition inside M03, chart draw-in (A20 +200), M06 drag feedback, count-up. Never cut M01, M07, the Reduced mapping, or the R07 and R08 changes.
Open conflicts: the motion rulings N16 to N19 contradict plan rulings C1, C2, C3, C7, C13 and D8 (and visual section 4, UX 7.6, login visual section 11); the designer did not apply them until the owner accepts. R05 must also emit the motion tokens, the initial bundle target moves from 575 to 583 kB and the global styles cap from 9 to 11 kB, and view-transition behaviour and low-end frame budgets are unmeasured (M07 must prove them on a throttled CPU and a real low-end phone before M03 is accepted).

## 3. The sequenced plan

### 3.1 Phases in detail

Slots: BE-1 (id and schema lane), BE-2 (feature lane), WEB-1, WEB-2, plus floating review, QA and PM runs (section 4). "CS n" is a client regeneration batch (3.3). "Baton" is the migration baton (3.2).

**P0 Land the in-flight work (WD 1 to 3).** BE-1: #44 fixes, re-review (money specialist, be-dba on the migration), merge; then #45 rebase and merge; then #46: after #44 is on `main`, remove and re-add its migration so it sorts after `AverageCostMarkersAndCostScale`, review, merge. BE-2: DC-BE1 and GUID PR-0 (documents and domain unit tests only, light). WEB: R01, then R02 (in flight); then CS1 plus a small web follow-up (ledger balance column, one low-stock rule) after #46. Housekeeping: triage the open Dependabot PRs: merge the dotnet group (#38) in a quiet gap before `AddPublicIds` (an EF Core bump changes generated migration code), defer the major tooling bumps (eslint 10 #42, stylelint 17 #37, lint-staged 17 #39, jsonc-eslint-parser 3 #40) until after M2 because they change lint rules under every slice. Exit: M1.

**P1 New look (WD 4 to 10).** WEB (2 slots): R05 (+60 lines, it now also emits the motion tokens) and R04 first, then R08 (+150, buttons with the new motion) and A01 (+100), then R06 and R11 (+80), then R25 (chain R05, R08, R11, R25 is 4.7 working days at best with the motion growth). The owner's answer to the motion questions must be in before R08 starts; otherwise R08 is built to the old rulings and reworked. Test lane: Q01 (stub-API harness). BE-1: GUID PR-1 (safety net plus SEC-11 as PR-1b), PR-2 (opaque cursors), PR-2b (tests and k6 treat ids as opaque, run by behavior-test-engineer and be-dba), then PR-3 (`AddPublicIds` and `AddPartnerCode`, one baton each). BE-2: DC-BE1 finish, DC-BE2 (recall and send-back endpoints), hardening part 1 (ER-06, ER-07, ER-08, ER-21). End of phase: QA round 1 (three personas, one at a time, about 2 hours) and gate G1 of the plan. Exit: M2.

**P2 Corrections and the id switch (WD 11 to 24).**
- WEB: DC-FE1 first (CS2 is done as soon as DC-BE2 and PR-2 are on `main`; NR-02 is a Major finding), DC-FE2, then L00, R07 (+100), M01 (motion foundation, Y lane, straight after R07), L01, the permission-derived profile helper (a 300-line slice, "P01-lite", before R10 so phone tabs and Home never cement role names, ADR 7.4), R10 (+120), R27, A02, A03, A04, R13 (+250, needs M01), R03, R12, L04, A05. That is 17 slices (R09 moved to P3 because it now needs M02); if the id switch is early, L02 and L03 (page SCSS, shell modes, no ids) follow. Test lane: M07 (motion harness) beside M01.
- BE-1: cut `feature/public-ids` from `main` after PR-3; PR-4 (leaf tables and Partner), PR-5 (User), PR-6 (Product), PR-7 (Document and Attachment), one after the other (they share test builders). Then PR-8 (web, CS3) with the freeze of lanes T and E, stores and `containers/catalog`/`stock`, about 1.5 working days. PR-9 merges the branch (3.4). PR-10: DBA re-measure of the 1M tier, IDOR and abuse retest by security, QA round 2.
- BE-2: owner answers recorded; RP-0 (oracle fixtures, after DC-BE2), RP-1, RP-2 (after PR-3 is on `main`; `Role` is a public-id table from day one); hardening part 2 (limiter split, ER-14 header, B20 limits and the over-limit report). DC-E2E (scenario file 30) when DC-FE2 is merged.
- Exit: M3.

**P3 Advanced tables, Home, motion (WD 25 to 38).** Right after PR-9: BE-2 does B1, B2, B3 (one day), then CS4. BE-1 starts RP-3 (access snapshot and epoch), then RP-4a and RP-5 in parallel with BE-2, then RP-4b and RP-6. WEB: lane T runs A06, R15 (+150), A07, R23, A08 (+120), A09 (+40), A10; the second web slot runs R14 (+80), R26, A16 (+120, needed by the roles screens), then R17 and R19 (Home) after CS4, then the motion slices M02, R09 (+200, after M02), M03, M04, M05; the test lane pulls Q02 and Q03 in. QA round 3 and the motion check on a real low-end Android phone and an iPhone (plan gate G1 addition). Exit: M4.

**P4 Configurable roles (WD 39 to 50).** BE: RP-6 and RP-7 finish, RP-8 (generated endpoint matrix, architecture tests), then B18 (unlock) with the Users screen. WEB: RP01 starts as soon as RP-5 is merged and a slot is free (expected about WD 32, inside P3), then RP02, RP03, RP04 with RP05, RP07, RP06 with RP09, RP08, RP10 (e2e 40 to 45). CS5 before RP02. QA round 4 with the edge persona leading (escalation, lockout, live rights change). Exit: M5.

**P5 Finish the refresh (WD 51 to 68).** The remaining slices run in the plan's order wherever a slot is free, from about WD 32: L02, L03, L08, Q11 (visual and matrix gates become blocking; MO-05 turns blocking at the plan's G3), R18, R20, A11, A12, A13, A14, A15, R29, R30, then M06 (table, list and editor motion, end of the critical path), Q05, Q06 (including the `master-data` cache version bump if not already done in PR-8), Q07, Q10, Q12, A17, A18, Q08, A21, Q04, L06, R22, A19, A20, L05, A22, L07, Q09, L09, M08 (Could, first to cut), last R24. Gates G4 to G7, QA round 5, native review of zh and ms, the iOS Safari and Android Chrome check, one release of soak before R24. R28 is dropped (superseded by EP03). Exit: M6.

**P6 Deployment (WD 69 to 72, but started early).** The hosting decision is needed by WD 10; with it, a spare slot builds the pipeline and a demo environment after M3 (about WD 25). Production readiness: `deploy.yml`, web headers per `docs/security/web-headers.md`, `Auth__ReturnRefreshTokenInBody=false` after the web uses the cookie (Q14), backup and restore drill, rollback to the previous tag, DAST triage. Exit: M7.

### 3.2 One linear migration order

Existing chain on `main`: `Initial` (20261009135544) through `AddDocumentReversedAt` (20261010145459). From here the order is below (MG1, MG2, ... are migration numbers, not milestones). A new migration is created only by the holder of the baton (one open PR with a migration at a time), after rebasing on `main`, and the creator is the agent who writes the entity change; be-dba reviews every one (Up, Down, up-down-up, data).

| # | Migration (name) | Epic and PR | Creator | When | Notes |
|---|---|---|---|---|---|
| MG1 | `AverageCostMarkersAndCostScale` (20261011030307) | EP01 #44 | be-framework (author), be-dba review | P0, first | Money precision and the average-cost markers. Exists on the branch now. |
| MG2 | `DocumentSearchIndexAndReversedBy` (20261011021255 today) | EP01 #46 | be-framework (author) | P0, after MG1 merges | Its timestamp is older than MG1's. After #44 merges: `dotnet ef migrations remove`, rebase, `migrations add` again so the new timestamp sorts after MG1; never hand-merge the snapshot. |
| - | (no migration) text limits, B20 | EP02 | be-framework | P2 | Default: validators and an over-limit report, no column change. Only if the owner chooses to shrink columns does `TightenTextLengths` take the slot after MG2, and only after the report shows zero rows over the new limit on Dev and QA. |
| - | (no migration) document corrections | EP03 DC-BE1, DC-BE2 | be-framework | P1 | The design says no new columns (recall nulls existing columns, send back reuses the Returned fields). Your brief expected a "document corrections columns" migration; the designer's text says none. If the DC-BE1 review finds one is needed, it takes this slot before MG3. |
| MG3 | `AddPublicIds` | EP04 PR-3 | be-framework, be-dba review (fill factor 90, backfill, `Down`) | P1, on `main` | `PublicId` on 9 tables, unique unfiltered index, soft-delete loop exemption. Inert until PR-4. |
| MG4 | `AddPartnerCode` | EP04 (ADR: optional unique `Partner.Code`, filtered index, null allowed) | be-framework | P1, on `main`, straight after MG3 | Moved from PR-4 so the integration branch contains no migration. |
| MG5 | `AddRolesAndPermissions` | EP06 RP-2 | be-framework, be-dba review, security-engineer sees the frozen seed | P2, on `main` after MG4 | `Role` is created as a public-id table from day one (10th table). Drops `User.Role` and `ApprovalRule.RoleRequired`. Rights-parity test on a fresh and a migrated database. |
| MG6 | `AddStockValueSnapshot` | B13, only if charts (A19, A20) stay in | be-dba | P5, after #44 is merged (the history needs correct average cost) | Optional. Charts are built last, so this is the last migration. |
| MG7 | `AddUserPreferences` | B9, only if owner answer P1 becomes yes | be-dba | after MG5 | Optional; default no. |

Rules that make collisions impossible:
1. The integration branch `feature/public-ids` contains no migration. All schema for the id switch (MG3, MG4) lands on `main` first; PR-4 to PR-7 are code only. Consequence: reverting PR-9 needs no database change.
2. One migration PR open at a time. The orchestrator hands the baton in HANDOFFS ("baton: EP04 PR-3"); the next migration PR does not start its `migrations add` until the previous one is merged.
3. Snapshot conflicts (`AppDbContextModelSnapshot.cs`, `*.Designer.cs`) are never merged by hand: remove your migration, rebase, add it again.
4. Add a test that fails when the model and the snapshot differ (`Database.HasPendingModelChanges()`); I found no such test in `tests/` today. Owner: integration-test-engineer, in P0 or with PR-1. Without it a forgotten `migrations add` is only caught at runtime.
5. Migration `Down` is tested, and the 1M-tier copy is used for any migration that touches a large table (none in the list above does).

### 3.3 DTO and OpenAPI changes, and when the web client is regenerated

The web client (`web/libs/shared/api`, `web/openapi`) is generated by `npm run api:generate` from a running API. Backend PRs never commit generated files. The web keeps compiling against the previous client until a batch regenerates it, because every change before CS3 is additive.

| Batch | Backend content that lands on `main` | Web consumer | Regenerated by |
|---|---|---|---|
| CS1 (end of P0) | #44, #45, #46: ledger balance after, low-stock rule fields, `reversedBy`, search; money rounding | small follow-up (ledger column, one low-stock rule) | fe-framework, in the follow-up PR |
| CS2 (P1, when DC-BE2 and PR-2 are on `main`) | recall and send-back endpoints, cancel body with reason, new `allowedActions` strings; opaque `cursor`/`nextCursor` instead of `afterId`/`nextCursor: long` | DC-FE1 and DC-FE2 | fe-framework, as the first commit of DC-FE1's branch, merged alone |
| CS3 (P2, inside PR-8 on the integration branch) | the whole id switch: ids as uuid strings, `DocumentLineDto.id` and `LedgerRow.id`/`AuditLogDto.id` removed, problem-details `productId` as string, `Partner.code` | ids to string in about 100 files, routes, stores | fe-framework, once, in PR-8 only |
| CS4 (start of P3, after PR-9, B1 to B3) | `returned` filter, ascending order option, `updatedAt` on list items | R17, R19 | fe-framework |
| CS5 (P3/P4, when RP-5 to RP-7 are on `main`) | roles, permissions, overrides, effective permissions, `PermissionCode` enum, `UserInfo` additive fields, `X-Permissions-Version`, approval rule `approverTier` | RP01 to RP08, Settings rule form | fe-framework |

Protocol for every batch (this is the rule against generated-client churn):
1. Merge the batch's backend PRs first. The orchestrator writes "contract freeze CSn" in HANDOFFS.
2. One agent (fe-framework) regenerates against the merged API, fixes type errors only, updates typed stub fixtures, and opens a PR that contains nothing else. It merges alone, with the usual review.
3. Any other web PR open at that moment rebases after the merge; a PR that touches the generated files itself is wrong and is sent back.
4. Never two regeneration PRs open at once, and never a regeneration while the id-switch freeze is in force except CS3 itself.
5. Between batches, the web does not consume a new backend field; if a slice needs a field earlier, it uses the plan's stated fallback.

Order of DTO changes in one line: money and QA fields (CS1), corrections and cursors (CS2), ids (CS3), document filters (CS4), roles (CS5). Each batch is additive except CS3 (a breaking change by design, hence the integration branch).

### 3.4 The integration branch for the id switch

- **Cut.** `feature/public-ids` is created from `main` right after PR-3 (the columns and the partner code are already on `main`, the API still speaks integers). Branch name follows the repository guard (`<type>/<name>`).
- **Stack.** PR-4 to PR-8 each target the integration branch, not `main`. CI and the PR guard run on any pull request, so each keeps its own green build, its 1,500-line limit and its independent review. The ruleset's required checks apply only to `main`, so the merge captain (orchestrator) enforces "CI green plus review verdict recorded" by hand for stacked PRs.
- **Sync with `main`.** The integration owner (be-framework) merges `main` into the branch (merge commits are allowed on this branch only) at three fixed points: before PR-4, before PR-8 (so the web starts from the final `main`), before PR-9; and whenever a PR marked "id-sensitive" lands on `main` (anything under `src/Ims.Application`, `src/Ims.Api`, `tests/Ims.Api.Tests` builders, `web/libs/features/**/services`, `web/libs/containers/{documents,catalog,stock}`, `e2e/src`). The merge captain names these in HANDOFFS.
- **Allowed on `main` meanwhile:** web slices that carry no ids (they only need the sync), RP-0 to RP-2 (no wire change), hardening that touches no DTO, docs. **Not allowed on `main` until PR-9:** any endpoint, DTO, handler or policy change keyed by id (RP-3 onward, B1 to B3, B18) and any regeneration of the client. Those wait, or are stacked on the integration branch with the owner's consent.
- **Web during the branch.** Slices keep consuming the old client (correct, because `main`'s API is unchanged). All H-marked slices wait. About 15 non-H slices remain available, so the web lanes do not starve. PR-8 is the only PR that regenerates from the branch API; it carries: client, ids to string, routes (`Number(id)` removed, lint rule against it), stores, specs, typed stub fixtures, `ngsw-config.json` master-data cache `version` bump, recents and preferences validation, e2e page objects (`savedId`). Freeze for lanes T and E, the stores, `containers/catalog` and `containers/stock` for about 1.5 working days; the other web slot builds shell, style or test-lane slices.
- **Merge (PR-9).** One squash PR from the integration branch to `main`. It will exceed the 1,500-line guard, so the product owner adds the `large-change` label (decision in section 8). Before merging, run the real-API e2e workflow by hand on the branch (`E2E (Playwright)`, up to 45 minutes; it never runs on pull requests) and the full integration suite in CI. After merging, PR-10 (1M-tier re-measure, IDOR retest, QA smoke).
- **Rollback.** Revert the merge commit; no database step because the schema is already on `main` and inert. The web and API are one release unit and are reverted together.

## 4. What runs in parallel, and what cannot

Capacity limit: 3 to 4 agents at once on this machine, at most 2 of them heavy. Heavy = anything that starts the API with SQL Server, runs the 629-test integration suite, runs a DBA measurement, or runs a QA persona or a real-API e2e against a live API. Web builds (lint, Jest, production build, stub Playwright) are CPU-heavy but need no SQL; they count as light for the SQL limit.

Slot model used in the estimates:

| Slot | Normal use | Heavy? |
|---|---|---|
| BE-1 | id and schema lane: money PRs, GUID chain, RP-3 onward | yes |
| BE-2 | feature lane: corrections, hardening, RP-0 to RP-2, RP-4, RP-5 | yes |
| WEB-1 | table lane T and anything else in the plan's order | no |
| WEB-2 | second web lane (E, S, D, H, Q), or the integration/flip work | no |
| floating | independent reviewers (10 to 20 minutes per PR), PM, designers, security; QA personas (about 40 minutes each, one at a time) | reviewers light; QA heavy |

When a QA persona or the DBA measurement runs, one backend slot is paused for that time. When both backend slots are busy, the web gets 2 builds a wave, not the 3 the refresh plan assumes; that is why the refresh plan's 9 to 10 weeks becomes a longer calendar here (section 5).

**Serial lanes (nothing in a lane runs beside another member of it):**
- Migrations: one baton (3.2).
- Contract: one regeneration batch at a time (3.3).
- Merge train: one merge to `main` at a time; the strict "branch up to date" policy re-runs CI after every other merge, so a ready PR waits for the one before it.
- GUID chain: PR-4, PR-5, PR-6, PR-7 (shared test builders), then PR-8 and PR-9.
- RP chain: RP-0, RP-1, RP-2, then RP-3, RP-4a (with RP-5), RP-4b (with RP-6), RP-7, RP-8.
- Refresh lanes from plan v2 section 2.2: T (table and list pages, the critical path), S (shell), E (document editor), U (sign-in), D (drawer), H (Home), F (form controls), V (overlays), Y (styles library), Q (e2e and CI tooling). New lane X (access: roles and person screens) from the roles UX.
- QA rounds: one persona at a time (shared QA database and ports).

**Can run in parallel:** a web slice with a backend PR (disjoint trees); two web slices on different lanes; the GUID chain with the corrections web slices; RP-1 and RP-2 with the GUID chain; PR-1, PR-2, PR-2b; the DBA 1M-tier run with web builds (but not with two backend slots).
**Cannot:** GUID PR-1 before #44 and #46 are merged; RP-4a or RP-3 beside PR-5 or PR-7; the oracle fixtures (RP-0) before DC-BE2; two migration PRs; a web slice that builds ids before PR-9; A16 or RP04/RP05 before CS3; R25 before R06, R11, A01; any lane-T slice beside another.

**Shared SQL and ports (so agents never overwrite each other's data).** Proposed fixed assignment, to be confirmed against `e2e/README.md` (which uses 5611/5612 as examples):

| Who | Database (instance `ImsDev`) | API port | Web port |
|---|---|---|---|
| BE-1 | `Ims_Dev` | 5601 | - |
| BE-2 | `Ims_Verify` | 5611 | - |
| WEB (stub e2e) | none (stubbed API) | - | 5622 |
| QA personas | `Ims_QA` (only they write it) | 5631 | 5632 |
| Real-API e2e, security retest | `Ims_QA2` | 5641 | 5642 |
| DBA 1M tier | `Ims_Perf` (exclusive; never while a QA persona runs) | 5651 | - |

CI uses a fresh database per run, so it never collides.

## 5. Elapsed-time estimates

Assumptions (stated so you can challenge them):
- A working day gives each slot about 6 productive agent-hours. With 4 slots and about 70% packing (waiting for CI and review, serial lanes, the 2-heavy limit, owner latency) the team delivers about 17 agent-hours of finished work a day.
- Per PR: CI about 5 minutes on the PR and about 5 minutes again after the final rebase (strict up-to-date policy), independent review 10 to 20 minutes (the gate in `PR-REVIEW.md` section 8; it also covers PRs under 200 lines that touch money, permissions, migrations, auth or CI), one in three needs a re-review of 10 minutes. That is 25 to 35 minutes of wall time per PR, mostly while the author is idle, and about 130 PRs in the programme, so about 60 agent-wait-hours spread over four slots. The merge train handles about 6 merges an hour at best; expected load is 3 to 4 a day, so it is not the bottleneck unless a rebase storm happens (a migration or a client regeneration). After Q02, Q03 and Q11 land, CI grows from about 5 to about 15 minutes per run (plan K26 target); that adds roughly 10 minutes twice per PR for the last 40 slices.
- QA re-test round: three personas, one at a time, about 40 minutes each (2 hours), plus triage; fixes are added separately (1 to 2 working days of one slot per round). Five rounds: after M2, M3, M4, M5, M6.
- **Rework allowance: 25% on the build effort of every phase.** Why: every agent change is checked twice by machines and people it cannot talk to (CI on a fresh Linux build, then an independent reviewer who has not seen its reasoning); the review gate was introduced precisely because agent tests share their author's blind spots; about one slice in three needs a fix round in the refresh plan alone; the strict up-to-date merge policy forces rebases; QA personas find defects in finished slices; and migrations and generated client changes cause avoidable rebase work. The allowance is applied to the base duration of each phase, not to QA gates, owner decisions or the hosting wait.

| Phase | Agent-hours (base) | Critical path, base working days | With 25% rework (incl. QA round and gate) | Typical | Range |
|---|---|---|---|---|---|
| P0 | 27 | 2.4 | 3.0 | 3 | 2-4 |
| P1 | 100 | 5.5 (chain R05, R08, R11, R25 is 4.7 with the motion growth, QA round 0.5, waiting for the motion answer) | 6.9 | 7 | 5-9 |
| P2 | 228 | 11.2 (GUID PR-4 to PR-10 is 9.9 base days; 17 web slices plus M01 and the R07, R10, R13 growth fit beside it) | 14.0 | 14 | 11-19 |
| P3 | 185 | 11.2 (lane T has seven serial slices, 8.8 with QA round 3; M02 to M05 and R09 share the second lane) | 14.0 | 14 | 11-18 |
| P4 | 130 | 9.6 (lane X serial path about 9, plus QA round 4) | 12.0 | 12 | 9-16 |
| P5 | 215 | 14.4 (about 35 slices over 3 web slots, M06 at the end of lane T, gates G4 to G7, QA round 5, soak) | 18.0 | 18 | 14-24 |
| P6 | 30 | 3.2 | 4.0 | 4 | 3-6 |
| **Total** | **about 915** | **about 57** | **about 72** | **72 (14 weeks)** | **55-96 (11 to 19 weeks)** |

Three scenarios for the total:
- Optimistic, 55 working days: rework 10%, no gate finds a Major, the id switch lands first time, the owner accepts the motion defaults on day one and answers everything within a day.
- Typical, 72 working days: the allowance above.
- Pessimistic, 96 working days: rework 40%, one gate sends a structural problem back (the ARIA table, risk K16 of the plan, or the lines grid), the id switch needs a second attempt (a week), the motion harness shows the frame budgets fail on the low-end phone and M03 and M06 are reworked (a week), hosting is decided late.

Compared with the refresh plan's own figure (9 to 10 weeks, 48 working days; 10 to 11 weeks with the motion file) this is longer for three reasons: it also carries the id switch, the roles epic (backend and screens), the corrections and deployment; the machine limit makes the web run 2 builds a wave while the backend is busy; and the rework allowance is 25%, not 15%. If the team works three days a week instead of five, multiply every calendar figure by about 1.6.

The first re-estimate is mandatory after P1 (WD 10): compare real slice throughput against the 5 hours and 1.1 slices per lane per day used here (this repeats plan risk K28).

## 6. Milestones

| M | Name | Target | Demo-able outcome | Acceptance check (all must be true) |
|---|---|---|---|---|
| M1 | Money and QA backend fixes merged | WD 3 | Reverse a 600 x 200 receipt: Stock value and average cost return to the figures before the receipt (PR-01). Low stock list, Stock "Low stock only" and Home agree (PR-04). Search finds a remark word (PR-05). Ledger shows balance after (PS-14). | #44, #45, #46 merged; CI green on `main`; each had its independent review with no open Blocker or Major; both migrations apply to an empty database and to a copy of `Ims_QA`, and `Down` of the last two works; money endpoints and the Excel export show exactly 2 decimals, half away from zero, and a script posting `12.345` gets the documented result (B4 closed); CS1 merged. |
| M2 | Sign-in page and new palette visible | WD 10 | The app in the new palette (light and dark), red required marker with "* means required", `12,400.00` everywhere, new buttons (with the first motion) and controls, the new sign-in page. | R01, R02, R04, R05, R06, R08, R11, A01, R25 merged; `npm run contrast` 162 checks, 0 failing; the sign-in acceptance list (LG-01 to LG-48) and `25-login` pass; RQ-01 to RQ-06 and FR-05 pass; no horizontal scroll at 320 px and 400% zoom; initial bundle under the 600/650 kB budget; QA round 1 with no open Blocker or Major. |
| M3 | Public ids and the correction workflow | WD 24 | URLs and API ids are GUIDs (`/receipts/<guid>`); a Staff user who sees a wrong quantity in the Post dialog taps "Edit items" and fixes the same document; a supervisor can send an approved document back or cancel it. | Architecture test "no integer id on the wire" green; IDOR matrix green (404 for other warehouses and unknown ids); cursors opaque; reconcile drill 0 differences; 1M-tier by-id and list endpoints under 1 s P95; PR-9 merged with real-API e2e 01 to 13 green; corrections criteria DC-01 to DC-16 pass in scenario 30 (NR-02 and PR-02 closed); migrations up-down-up including `AddPublicIds`, `AddPartnerCode`; QA round 2 with no open Blocker or Major. |
| M4 | Advanced table and Home screens | WD 38 | Lists with saved views, filter chips with URL state, bulk selection with export, row expansion, continuous paging where supported; role-aware Home for Doers, Reviewers, Overseers, Admin; route and theme transitions, Home and sign-in motion. | A06, R15, A07, R23, A08, A09, A10, R17, R19, M02 to M05 merged; MO-01 and MO-02 green and the frame gate (MO-05) report-only numbers recorded on a throttled CPU and a real low-end phone; table acceptance (TB-*) and the touched registry screens pass axe in light and dark; `13-cost-visibility` green and the DOM money scan clean for Staff and Clerk on every new surface; Home call budget RA-09 holds; QA round 3 with no open Blocker or Major. |
| M5 | Configurable roles | WD 50 | An administrator creates a role "Report viewer" (copy of Clerk plus `reports.read`), a user in it, signs in as that user and sees exactly those menus; removes `reports.read` and the user's next navigation is forbidden without signing out; a deactivated user is refused on the next request. | RP-0 to RP-8 and RP01 to RP10 merged; rights-parity test (oracle) green on a fresh and a migrated database; last-administrator race test and escalation matrix green; ER-15 and SEC-06 closed with a measured freshness under 3 seconds across two hosts; `grep` finds no role-name literal in `web/libs` and the architecture test bans role comparisons in Domain, Application and Api; e2e 40 to 45 green; QA round 4 (edge persona leading) with no open Blocker or Major. |
| M6 | Full 2026 refresh and the quality gate | WD 68 | The complete refresh: palette command, drawer, notifications, offline banner, charts if kept, print, forced colours, shortcuts, table and editor motion. | All 81 slices (73 plus M01 to M08) merged or cut by an explicit decision (R28 is dropped, superseded; M08 is the first cut); MO-01 to MO-09 pass and MO-05 is blocking; gates G4 to G7 passed; visual and matrix gates blocking (Q11); bundle budgets met; full real-API e2e suite green; the three personas make a final pass with no open Blocker or Major; zh and ms reviewed by a native speaker or still marked `TODO-review` with the owner's consent; iOS Safari and Android Chrome checklist done; R24 clean-up merged after one release of soak. |
| M7 | Deployment | WD 72 | The released image runs on the chosen host and passes the smoke check; Staff use it on a phone. | Hosting decided and recorded; `deploy.yml` deploys a `release.yml` image to the target with the smoke check and a rollback to the previous tag tested; web headers applied and verified; `Auth__ReturnRefreshTokenInBody=false` after the web uses the cookie (Q14); backup and restore drill done; demo seeding off; DAST first run triaged; no secrets in the repository. |

## 7. Risk register (top 10)

L = likelihood, I = impact (H/M/L).

| # | Risk | L / I | Mitigation | Owner |
|---|---|---|---|---|
| 1 | Migration collisions: #44 and #46 already have out-of-order timestamps; the snapshot file conflicts on every concurrent migration; EF Core bumps (Dependabot #38) change generated code | H / H | The baton and the order in 3.2; the id-switch branch holds no migration; remove-and-re-add on conflict; pending-model-changes test; merge the NuGet group in a quiet gap before MG3; be-dba reviews all | orchestrator (baton), be-dba |
| 2 | Generated web client churn: one huge generated file, regenerated by several PRs, conflicts and surprise type changes | H / M | Five named batches, one agent, nothing else in the PR, merged alone, others rebase (3.3); typed stub fixtures turn type changes into compile errors | fe-framework |
| 3 | Merge-train mistakes: strict up-to-date policy, squash-only (a stacked PR whose base was squashed must be retargeted), PR-8 or a stacked PR accidentally targeting `main`, six Dependabot PRs in the queue, the one over-limit PR-9 | M / H | One merge captain (orchestrator) and a written merge order in HANDOFFS; stacked PRs target the integration branch; defer Dependabot majors until after M2; `large-change` label decided in advance; revert plan (3.4) | orchestrator |
| 4 | Shared SQL and ports: agents sharing `Ims_Dev`, `Ims_QA` or a port overwrite each other's data; leftovers of earlier work (`Ims_Perf`, `Ims_Verify`, a stray `Ims.Api` on port 5187) | H / M | Fixed assignment table in section 4; QA database written only by QA personas; DBA measurement exclusive; each agent states database and port in its HANDOFFS entry; CI uses fresh databases | orchestrator |
| 5 | Scope creep: 73 slices already grew to 81 with the motion file (+6,680 lines) in one day; roles UX asks beyond the ADR, QA findings turned into slices, plus the owner's new ideas | H / H | Scope is this document; a new item enters only via the project-manager with a size and an epic; QA findings go to the next phase's backlog, not into the running slice; the cut order (plan 2.7: charts, polish, virtual scroll, table extras, drawer, spreadsheet lines, offline cache; motion file 7.5: M08, shared-element transition, chart draw-in) is the pressure valve; re-baseline at WD 10 | project-manager |
| 6 | Reviewer false negatives: reviewers and authors are the same kind of agent, tests share blind spots, the reviewer gets a diff and not the system | M / H | Specialist reviewers by topic (money, auth, migrations, CI); the oracle parity test for roles; 1M-tier re-measure and reconcile drill for ids; real-API e2e nightly and weekly; QA personas at every milestone; "would the test fail if the code were wrong" is a review question; the project-manager spot-reads one merged PR in five in review mode | orchestrator, project-manager |
| 7 | Token and context limits of long agent runs: the refresh plan is about 100,000 tokens, each ADR 25,000 or more, a 1,000-line slice needs many reads | H / M | One slice per agent run; the orchestrator passes only the slice block, the collision rows and the definition of done (by section or line range, not the whole file); stop and split at 1,000 lines; hand-offs written before a run ends; no agent edits a plan it was not asked to | orchestrator |
| 8 | Node 22.6 on the dev machine versus the required 22.22.3+ (CI uses Node 22 latest; this shell reports Node 20.x): needs `NODE_OPTIONS=--experimental-require-module` and Prettier `--end-of-line auto`, so results differ between local and CI | H / M | Install Node 22.22.3 or newer in P0 (about 30 minutes) and add an engines check; until then rely on CI as the judge and have agents state the command flags they used; fe-architect keeps the flags in `web/README.md` | orchestrator |
| 9 | Public repository data hygiene: the repo is public, and QA data, demo users, test passwords in workflows, screenshots for visual baselines, perf seeds or backups could expose customer or personal data | M / H | `docs/private/` is git-ignored; the guard blocks backups and secrets; secret scanning and push protection on; visual baselines come from demo data only and live on an orphan branch; reviewers check docs for real names; no customer data on any dev machine path that is committed; deploy secrets only as environment secrets | security-engineer |
| 10 | GitHub plan limits: rulesets on a private repository need Pro or Team (GIT-WORKFLOW); Actions time and storage, 20 concurrent jobs, artifact and cache quotas; CI growing from 5 to 15 minutes per run; weekly and nightly e2e, three-engine smoke and baseline storage | M / M | Keep the repository public while hygiene holds; decide the plan before any move to private; budget the CI jobs in Q11; baselines on the orphan branch with a size cap; nightly runs only `@smoke`; re-check usage after Q11 | orchestrator |

## 8. Decisions

### 8.1 Already taken (do not reopen)

1. Custom `app-*` components, no UI library and no bake-off; Angular CDK as behaviour primitives only inside `libs/shared/ui` (plan N1, N2).
2. The full plan: 73 slices (the cut list is only a pressure valve).
3. Public ids: the hybrid. Integer clustered keys stay; a random version-4 GUID `PublicId` on 9 tables (User, Warehouse, Partner, Category, Uom, Product, ApprovalRule, StockDocument, Attachment) with unique, unfiltered, non-clustered indexes; opaque encrypted cursors; no row id on ledger and audit responses; 12 PRs, about 85 agent-hours; `Partner.Code` added.
4. GUID D1 = no (no offline creation), D2 = yes (ids must not be guessable or countable); idempotent create (PR-7b) is optional and dropped by default.
5. No GUID primary keys on the ledger, audit, lines or balances, in any form.
6. Money: exactly 2 decimals on screen and in the API, rounded half away from zero, "RM" only in labels; the average cost is derived per base unit with up to 4 decimals; quantities keep up to 4 decimals.
7. Cost visibility by permission, not by role: Staff never see cost or value; Clerk sees cost (`cost.view`) but not valuation (`valuation.view`); configurable later through roles, with audit.
8. The independent review gate before merge (PR-REVIEW section 8) for every PR over about 200 lines and for any PR touching money, permissions, authentication, migrations, posting or CI/CD.
9. Taste questions (look, wording, motion details) are decided by the designers (ui-ux-designer, fe-designer), not the owner.
10. The repository is public and `main` is protected by a ruleset: PR required, four required status checks, strict up-to-date, squash only, linear history.
11. NV-27 date field `dd MMM yyyy` (R23); no bulk state-changing actions (N8); commands are never queued offline (N9); no drafts in browser storage (P9 default).
12. Document corrections design is "Decided" by the ui-ux-designer (recall, send back, cancel on Approved); R28 and B19 are superseded by DC-BE1, DC-BE2, DC-FE1, DC-FE2.

### 8.2 Open owner decisions, with my recommended default

| # | Decision | Recommended default | Needed by | Cost of waiting |
|---|---|---|---|---|
| O1 | Answer first: corrections confirmations (only the approver undoes an approval; Owner and Admin may send back or cancel a Large approved document; reason of 5+ characters required to cancel an Approved document) | Yes to all three | WD 5 | DC-BE1 codes the defaults; a later change is a small edit |
| O2 | Answer first: roles ADR Q1 to Q7 and the UX file's Q1 to Q8, merged: one role per person plus exceptions; a granted exception must end within 180 days, a restriction may be permanent; only Admin holds `roles.manage` at install; cost visibility configurable by administrators with a reason and audit; two approval levels; refuse to deactivate a role in use; "sees all warehouses" set per person | Take the ADR's answers (the UX file says the ADR wins). Conflicts to settle: warehouse scope per person (ADR) versus per role (UX Q8) -> per person; exception end date mandatory for grants (ADR) versus optional (UX Q5) -> mandatory for grants | WD 8 (RP-0) | RP-0 and the oracle fixtures wait; roles slip one-for-one |
| O3 | Answer first: hosting direction | One small VM with Docker Compose and Caddy (about US$6 to 24 a month), fits one deployment per customer; the free-tier route only for a data-free demo; revisit managed cloud (about US$100 to 200 a month, always on) when a customer needs availability guarantees | WD 10 for a demo environment; WD 60 for production | The demo environment and M7 slip; no other work waits |
| O4 | The `large-change` label for the one id-switch merge (PR-9) | Yes, applied by the owner on the day, after the real-API e2e passes | WD 22 | PR-9 cannot merge |
| O5 | Text limits (B20, question P12): tighten to 120 characters for partner and product names and 80 for other names for new input, existing data keeps working, an over-limit report instead of silent truncation | Yes, no schema change | WD 14 | None; L00 renders today's limits regardless |
| O6 | Charts (A19, A20) and polish (A21, L09, R22, L07): keep or cut; with the motion file the cut list also gains M08 first, then chart draw-in and M06 drag feedback | Keep, built last; ask again at WD 60 (the cut removes B12 to B14 and migration MG6) | WD 60 | About 7 working days saved if cut |
| O7 | Answer first: motion questions M1 to M8 of `2026-refresh-motion.md` (in-app "reduce animations" setting, press scale 0.97 and 2 px hover lift, Home count-up on integers, shared-element transition, pull to refresh, dialog overshoot, sign-in entrance on every load, automatic Lite mode) and the supersession of plan rulings C1, C2, C3, C7, C13, D8 | Accept the designer's defaults (taste is the designers' decision, 8.1 item 9; the owner confirms the supersession once after opening `motion-demo.html`) | WD 4, before R08 starts | R07 and R08 are built to the old rulings and reworked (about 250 lines) |
| O8 | Remaining refresh questions P1 to P11 | Use the plan's defaults (per-browser preferences, no bulk actions, browser CSV, strict chart numbers, no company name on prints, side peek for non-drafts, manual device check, no draft restore, keep duplicate lines, no privacy link) | per slice | none |
| O9 | `Partner.Code` (optional unique code) | Yes, 3 hours, in `AddPartnerCode` | WD 7 (PR-3) | Moves one migration |
| O10 | Native speakers for zh and ms review (NV-28, K15) | Named by G6; until then `TODO-review` stays | WD 60 | M6 acceptance wording |

### 8.3 Conflicts and corrections found while building this roadmap

Each is also in HANDOFFS.md. None is silently resolved.

| # | Conflict | Proposed resolution | Addressed to |
|---|---|---|---|
| C1 | #46's migration timestamp is older than #44's | Re-create after #44 merges (3.2) | be-framework, orchestrator |
| C2 | The brief expects "document corrections columns"; the design says no migration | Slot reserved, expected unused | be-framework (DC-BE1 review) |
| C3 | Plan v2 puts R17 (H, ids) in the fast path, wave 4; the owner wants slices that carry ids to wait | R17 and R19 move behind CS3 and CS4; R27 (NR-01, the top novice finding) takes its place in the early waves; the fast path is nine slices plus R27 | fe-architect (renumber waves) |
| C4 | Plan v2 assumes three web builds per wave; the machine runs 4 agents, 2 heavy | Waves are two wide while both backend slots are busy (section 5) | fe-architect |
| C5 | Plan v2 still calls the GUID ADR "Proposed" and prefers a flip after wave 4 or 9; the ADR is Accepted and has its own PR plan | Flip at the P2 end (about plan wave 9), plan sections 1.7 and 2.5a updated | fe-architect |
| C6 | Plan R28 and backend B19 versus the corrections design | Superseded; R28 dropped | fe-architect, ui-ux-designer |
| C7 | Roles ADR web slices P01 to P04 versus roles UX slices RP01 to RP10 | EP07 uses RP01 to RP10; P01's profile helper is built early as "P01-lite" so R10, R17 and R19 do not cement role names | fe-architect, be-architect |
| C8 | Roles UX section 10 asks for things the ADR does not have: dry-run for every access change, templates, person's access API with sources, role-level warehouse access, optional exception end date, an approval-level attribute | ADR wins (the UX file says so); the dry-run endpoint is the one real gap, about 600 lines and 10 agent-hours that are NOT in the 137 hours; be-architect decides in RP-0 | be-architect, ui-ux-designer |
| C9 | The request lists "RP-1 to RP-7, about 137 hours"; the ADR's 137 hours cover RP-0 to RP-8 (RP-1 to RP-7 alone are 120) | Plan with RP-0 to RP-8; RP-9 optional | be-architect |
| C10 | RP-3 onward and the id switch rewrite the same handlers, `UserInfo` and policies; the ADR says only "RP-5 to RP-7 after PR-7" | RP-3 onward waits for PR-9 (3.4) | be-architect |
| C11 | The refresh plan lists Q06's cache version bump under H; the GUID ADR puts it in PR-8 | Do it in PR-8; Q06 then only checks it | fe-architect |
| C12 | Real-API e2e never runs on pull requests, so the id switch has no automatic end-to-end gate | Manual dispatch on the integration branch before PR-9 (3.4); make `e2e.yml` required weekly (still open from the behaviour-test hand-off) | behavior-test-engineer, orchestrator |
| C13 | The motion file supersedes plan rulings C1 (shimmer replaces pulse), C2 (press scale), C3 (instant hover), C7, C13 and D8, but did not apply them; it also asks the owner taste questions that decision 9 gives to the designers | Treat the designer's defaults as the working rule once the owner confirms the supersession (O7); fe-architect applies motion file section 7 to the plan (waves 5b, 6b, 8b, 19b, 20b, rulings N16 to N19, budget moves) | fe-architect, ui-ux-designer, fe-designer |
| C14 | Motion file counts "72 slices, 48,660 lines" for plan v2; plan v2 is 73 slices and 49,260 lines since L00 | Roadmap uses 73 + 8 = 81 slices and 55,940 lines | fe-architect |
| C15 | Motion grows R05 (tokens), R07, R08, R11, A01, which are already first in the queue, and moves R09 behind M02 and R13 behind M01 | R05, R08, A01, R11 carry the growth in P1; R09 moves to P3; M01 is built right after R07 | fe-architect |

## 9. How this file is kept true

- The project-manager updates `TASKS.md` and the phase table after every merge or gate, and re-estimates at WD 10 (after P1), WD 24 (M3) and WD 50 (M5) using real throughput.
- The orchestrator writes the baton, the contract freezes and the id-sensitive merges in `docs/team/HANDOFFS.md`; this file only defines the rules.
- A change in scope, a finished epic or a decision from section 8.2 changes this file in the same working day. Open items older than their "needed by" day are reported as blockers.
