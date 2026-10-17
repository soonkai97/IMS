# IMS lean scope: what we build, what waits, what is dropped

Author: fe-architect. Date: 2026-10-12. Status: proposal for the product owner to approve. Nothing else was edited: the plan (`docs/design/2026-refresh-plan.md`), the roadmap and the ADRs stay as they are until the owner says yes, then the project-manager re-baselines.
Trigger: the owner's instruction "make sure no over engineering". Plan v2 grew to 98 slices, 68,840 lines and 34 waves; this file judges every slice and epic against a real requirement.
Read for this review: the plan (wave table, programmes R, A, Q, L, M, RP, G, fast path, cut list, backend items), `docs/pm/ROADMAP.md`, `adr-guid-keys.md`, `adr-roles-permissions.md`, `roles-permissions-ux.md`, `document-corrections.md`, `2026-refresh-motion.md`, `mvp-ux.md`, and the code under `web/libs/shared/ui` (the table is a native `<table>`, 670 lines with its pager; `@angular/animations` is in `package.json` and imported nowhere).

## 0. The answer in one table

| | Plan v2 and roadmap | Lean | Change |
|---|---|---|---|
| Frontend slices | 98 | 32 | -67% |
| Frontend lines (changed, with specs) | 68,840 | about 17,600 | -74% |
| Waves | 34 (at most 3 builds) | 16 (at most 3 builds, usually 2) | -53% |
| New dependencies / tooling | `@angular/cdk`, `jest-axe`, `@lhci/cli`, orphan-branch visual baselines, 3-engine PR matrix | none (one dependency removed: `@angular/animations`) | |
| Real-API e2e scenario files reserved | 22 (14 to 28, 30, 40 to 45) | 4 new files, 6 existing ones extended | |
| Agent-hours, whole programme (roadmap basis, review included) | about 915 (about 1,145 with 25% rework) | about 415 (about 520 with 25% rework) | -55% |
| Typical calendar | 72 working days (range 55 to 96) | about 38 working days (range 30 to 50) | -47% |

Of the 98 slices: 29 are ESSENTIAL (18,920 lines as written), 33 are VALUABLE (23,960), 36 are GOLD-PLATING (25,960). The lean plan trims the ESSENTIAL slices too (they carry growth that serves gold-plated features: motion modes, density tokens, container-query machinery, CDK) and builds a few VALUABLE ones because the owner asked for "more advanced tables" and a tidy phone UI.

The owner asked for eight things. Everything below traces to one of them or to a bug or security fix:

| # | Stated requirement | Where it is met in the lean plan |
|---|---|---|
| RQ1 | Nicer modern UI, animation, responsive | R01, F1, R08, R10, R11, R13, L02, R25 (look, motion, layout), R29+R30 (phone) |
| RQ2 | More advanced tables | A02 (sticky header and first column, cards by width, row-action names), TB (page size, First/Last, column show/hide), A07 (filters in the URL with chips) |
| RQ3 | Polished sign-in page | R25 (with its entrance and one shake), R26 (re-sign-in in place) |
| RQ4 | Correct a submitted document | R28 (recall, Edit button), R31 (cancel on Approved, with a reason) |
| RQ5 | Configurable roles, permission pages, maybe per-user exceptions | RP00, RP01, RP02, RP03, RP05+RP11; exceptions deferred (section 6.1) |
| RQ6 | Non-guessable ids in URLs | G01, G02 plus the backend hybrid (section 6.2) |
| RQ7 | Money with 2 decimals | R04, R33, backend PR #45 |
| RQ8 | Cost hidden from Staff | already built; kept by the rule "decide by permission" (RP00, RP01, test re-keying in RP-8) |

Bugs and security fixes that are kept regardless of any requirement: L00 (sideways scroll, owner-reported), R27 (NR-01, Major), R26 (ER-01, Major), R02 (required marker, accessibility defect), R03 (reports), R11 (double announcements, toast covering Save), SEC-11 (404 for out-of-scope ids), ER-15 (rights lag after a demotion), the backend hardening items ER-06, ER-07, ER-08, ER-14, ER-21.

## 1. Principles and the review checklist

1. A slice exists only because the owner asked for it, or because it fixes a bug or a security finding. "Would be nice" waits for a user to ask.
2. Use what the platform and the code already give us (native `<dialog>`, HTML `popover`, CSS container and view-transition features, native `<table>`, the existing `app-*` set) before adding a library, a directive family, a mode or a service.
3. No configuration surface nobody will change: one motion level (the OS "reduce motion" setting is honoured), one density, one theme store, no per-user switches for what a designer can decide.
4. Build for the data we have: server paging caps a page at 200 rows, so no virtual scroll; one API instance (the hosting default of the roadmap), so no cross-instance cache machinery; two renderings of a list (table and card), not three.
5. Process is sized to risk: one real-API suite, one fast browser smoke on every pull request, the independent review the owner already decided on; no extra gates, baselines or matrices until a release shows they would have caught something.

The one-line checklist every reviewer applies to every slice and every PR:

> Which stated requirement needs this? Is there a simpler way? Does it add a dependency or a configuration surface?

A slice that cannot answer the first question goes to the deferred list with a trigger. A slice that adds a dependency or a setting needs the owner's name on it in the PR.

## 2. Every slice and epic, classified

Class: E = ESSENTIAL (stated owner requirement, or bug or security fix), V = VALUABLE (helps users, not requested, can wait), G = GOLD-PLATING (speculative). "Lines" is the size in plan v2. Actions: Build (as lean slice, new size in brackets), Merge (folded into another slice), Defer (section 4, with trigger), Drop (section 5).

### 2.1 Programme R: Refresh (32 slices, 19,750 lines)

| Slice | Class | Reason | Lines | Action |
|---|---|---|---|---|
| R01 Palette, aliases, contrast gate | E | The "nicer UI" in one cheap step; the contrast gate protects everything after it | 310 | Build [450] with R06's no-flash script and the tightened bundle budget (absorbs Q04) |
| R02 Required marker, legend, login behaviour | E | Known accessibility defect | 600 | Build [550] |
| R03 Reports fixes | E | Bug PR-03, PR-06 | 350 | Build [350] |
| R04 Money format and input rules | E | RQ7; also ER-12 quantity rule | 450 | Build [450] |
| R05 Non-colour token foundation, icons | E | Tokens and icon stroke are needed; the density token block is not | 660 | Merge into F1; drop the density tokens |
| R06 Preferences, theme per user | V | A per-user store serves features we defer; only the no-flash script is needed | 700 | Simplify: no-flash script and PWA colours into R01 [140]; the store waits |
| R07 Reduce-motion contract, skeleton | E | The infinite-animation bug and the loading primitives | 450 | Merge into F1 |
| R08 Buttons and form controls | E | The most visible part of the refresh (press, busy, Saved) | 900 | Build [700]: drop the hover overlay layer, the three-layer label stack and the shake token |
| R09 Chips, stat cards, tabs | V | Chips already carry icon and text; the palette aliases restyle them | 500 | Merge into R08 and R10 [100, includes ER-18] |
| R10 Shell, tab bar, action bar | E | Responsive and modern; ER-09 | 520 | Build [550] with the route fade (one `withViewTransitions` line plus CSS) and the progress bar |
| R11 One failure, one announcement | E | Real QA findings (EA-15, EA-26, ER-02, ER-11) | 930 | Build [650]: drop the FLIP of surviving toasts and extra toast rules |
| R12 Density | G | Nobody asked; a compact mode for imaginary power users | 500 | Defer |
| R13 Overlays: dialog, sheet, toast, banner, card, empty | E | Visible animation of what users touch most | 850 | Build [600]: dialog pop, sheet slide; drop the swipe gesture, banner disclosure, empty-state entrance |
| R14 Saved state, row highlight, live regions | V | ER-03, ER-04 (Minor); "Saved" in place is pleasant | 780 | Merge with R15 [750 together]; drop the "{n} things to fix" component |
| R15 Skeleton shapes per screen | V | Layout stability and ER-05; perfect shapes are polish | 800 | Merge with R14; skeletons on the six main screens only |
| R16 Command palette component | G | Not requested | 500 | Defer |
| R17 Home for Doers | V | NV-08 (tiles not tappable) is a real finding; five Home profiles are not requested | 900 | Build as HOME [450]: tappable tiles, returned drafts, restyled cards for every role |
| R18 Palette logic and recents | G | Not requested | 750 | Defer |
| R19 Home for Reviewer, Overseer, Admin | G | Per-role Home variants not requested | 900 | Defer (with B1 to B3) |
| R20 Palette search | G | Not requested | 700 | Defer |
| R22 Keyboard shortcuts | G | Could; not requested | 500 | Drop |
| R23 Date field dd/MM/yyyy | V | Accepted earlier; a native date input works | 600 | Built since (PRs #96, #98, #100): the owner chose `dd/MM/yyyy` everywhere |
| R24 Clean-up | E | Removes the alias layer | 300 | Build [200]; also removes `@angular/animations` |
| R25 Sign-in page | E | RQ3 | 1,000 | Build [750]: language as a segmented control, no app-menu, no theme menu, no version footer, no caps-lock hint; entrance and one shake included |
| R26 Re-sign-in in place | E | Major ER-01 | 450 | Build [400] |
| R27 Quantity entry, double-save safety | E | Major NR-01, NR-07, NR-08, NR-09 | 450 | Build [450]; first in the queue |
| R28 Recall, Edit button, Post dialog | E | RQ4 | 750 | Build [600] |
| R29 Phone document screens | V | NR-03, NR-04, NR-12, NR-16: the phone is the main device | 700 | Merge with R30 [750 together] |
| R30 Phone lists, details, wording | V | NR-05, 13, 14, 15, 17, 18, NV-25 | 600 | Merge with R29; drop the zh/ms wording rework (needs the native review anyway) |
| R31 Send back, cancel on Approved | E | PR-02: cancel on Approved with a reason | 650 | Build [450]; send back deferred |
| R32 Corrections e2e | G | A separate slice for tests | 500 | Drop the slice; one scenario file inside R28 and R31 [150 counted there] |
| R33 Money and QA backend follow-up | E | Ledger balance, one low-stock rule (PR-04) | 200 | Build [200] |

### 2.2 Programme A: Advanced UI (22 slices, 17,720 lines)

| Slice | Class | Reason | Lines | Action |
|---|---|---|---|---|
| A01 CDK foundation, popover | G | A new dependency plus an overlay-in-dialog workaround; the HTML `popover` attribute and native `<dialog>` cover the few popovers we need | 650 | Drop |
| A02 Table core: ARIA grid rewrite | E | "More advanced tables": sticky header, sticky first column, cards by width, row-action names (ER-10). Keep the native `<table>`; the ARIA grid rewrite is the plan's own highest accessibility risk (K16) | 1,000 | Build [650] on the native table: CSS sticky, one container query, ER-10 labels |
| A03 Column visibility, resize, reorder | V | Show/hide is useful; resize, reorder and drag-drop (a 40 kB CDK chunk) are not | 1,000 | Build the show/hide menu inside TB [300]; defer the rest |
| A04 Keyboard grid navigation | G | Roving tabindex grid for a table that has links; ER-10 labels moved to A02 | 800 | Defer |
| A05 Paging toolbar | V | PS-13 (First/Last, page size) | 600 | Build inside TB [300] |
| A06 Saved views and multi-sort | G | Needs backend B8; speculative | 900 | Defer |
| A07 Filter chips and URL state | V | Back button and shareable links; the concrete "advanced table" win | 900 | Build [500]; no dependency on A06 |
| A08 Row expansion | V | Peek at lines without opening; a third rendering of the same data | 620 | Defer |
| A09 Bulk selection, floating bar | G | Plan's own N8 allows read-only bulk actions only | 840 | Defer |
| A10 Export CSV | G | The four server Excel exports exist; a client CSV is a second path that must obey cost rules | 600 | Drop |
| A11 Virtual scroll 10,000+ rows | G | Server paging caps a page at 200; needs B7 | 1,000 | Drop |
| A12 Combobox | V | The product picker works; wording fixes go to R30 | 840 | Defer |
| A13 Combobox adoption | V | Same | 500 | Defer |
| A14 Inline lines grid | G | Spreadsheet entry; warehouse staff use phones | 1,100 | Drop |
| A15 Paste and grid integration | G | Needs B11; same reason | 900 | Drop |
| A16 Side drawer peek | V | Nice for approvers; the roles design no longer needs it | 920 | Defer |
| A17 Document timeline | V | Needs B10 for names and times | 640 | Defer |
| A18 Notifications panel | V | Home and the sidebar badge already show waiting approvals | 760 | Defer |
| A19 SVG chart primitives | G | Charts not requested; also needs a migration (MG6) and B12 to B14 | 1,000 | Drop |
| A20 Home charts | G | Same | 900 | Drop |
| A21 Empty-state illustrations | G | Decoration | 450 | Drop |
| A22 Breadcrumbs, page header | G | A two-level app; the page header exists | 800 | Drop |

### 2.3 Programme Q: Stable (12 slices, 8,220 lines)

| Slice | Class | Reason | Lines | Action |
|---|---|---|---|---|
| Q01 Stub-API harness | V | A browser smoke on every PR without SQL Server catches layout and axe regressions that Jest cannot | 900 | Build as Q1 [600] with the axe check of Q03, a reduced-motion check and a weekly WebKit project: 8 screens, not 16 |
| Q02 Visual regression, orphan-branch baselines | G | A manual-update workflow, pinned images and a data branch to guard against a risk we have not yet had | 700 | Drop; Q1 uploads screenshots as a CI artifact for the designer |
| Q03 axe matrix, 3-engine PR smoke | G | Firefox on every PR guards a browser our users do not use | 600 | Drop; the axe part is in Q1, WebKit runs weekly |
| Q04 Bundle budgets | V | Config, not a slice | 350 | Merge into R01 (budget 600/650 kB) |
| Q05 Global error handling, GET retry | V | Useful; today's interceptor already handles the common cases | 750 | Defer |
| Q06 Offline banner, read cache | V | The PWA already caches master data; unproven need | 800 | Defer |
| Q07 Lint: tokens, inline styles, imports | G | The hex rule is in R01; the rest guards CDK and libraries we no longer add | 700 | Drop |
| Q08 Contract tests, controls (jest-axe) | G | A new dependency; every slice already writes its own specs | 700 | Drop |
| Q09 Contract tests, rest | G | Same | 800 | Drop |
| Q10 Gallery `/__gallery` | G | A dev-only route plus a "not in production" check | 760 | Drop |
| Q11 CI wiring, triage rules | G | Wiring for gates we do not build | 400 | Drop; Q1 edits `ci.yml` itself |
| Q12 Interaction, CLS, Lighthouse budgets | G | Adds `@lhci/cli`; exists for virtual scroll | 760 | Drop |

### 2.4 Programme L: Responsive (10 slices, 7,500 lines)

| Slice | Class | Reason | Lines | Action |
|---|---|---|---|---|
| L00 Content containment | E | Owner-reported defect (Partners list scrolls sideways) | 600 | Build [500] |
| L01 Layout system foundation | V | Size classes, bands and a width directive are machinery; two container queries and a few tokens are enough | 800 | Merge into L02 |
| L02 Layout applied to screens | E | RQ1 responsive | 1,000 | Build [600] with L01: page frame, fluid spacing, fixes at 320, 360, 768, 1280 |
| L03 Shell behaviours: collapsible rail, keyboard | V | The keyboard-aware tab bar is in R10; the rail is a desktop nicety | 1,000 | Defer |
| L04 Table-to-card by container width | V | The card switch is one container query in A02 | 900 | Merge into A02 |
| L05 Touch and pointer rules, zoom fixes | V | Fix what the matrix reports; there is no matrix | 600 | Merge into L02 and R10 |
| L06 Forced colours, high contrast | V | Valid, but no one asked | 600 | Defer |
| L07 Print stylesheets | V | Real only if users print receipts | 700 | Defer |
| L08 Responsive test matrix automation | G | 8 widths plus zoom, nightly; replaced by Q1 at 3 widths | 800 | Drop |
| L09 Large screens, foldables | G | Speculative devices | 500 | Drop |

### 2.5 Programme M: Motion (8 slices, 4,550 lines, plus 2,130 growth inside other slices)

| Slice | Class | Reason | Lines | Action |
|---|---|---|---|---|
| M01 Motion foundation | E | Tokens and the reduced-motion contract | 650 | Merge into F1 as 3 durations and 2 easings; drop `data-motion` full/lite/reduced/off and the in-app setting, keep the OS setting and one `off` switch for tests |
| M02 Motion primitives (disclosure, reveal, indicator, FLIP, count-up) | G | A directive family for effects no one asked for | 900 | Drop; the tabs indicator is CSS in R10 |
| M03 Route and theme transitions, top bar | V | A route fade is the cheap visible win; theme crossfade and shared-element transition are not | 650 | Merge into R10 (route fade, progress bar); drop the rest |
| M04 Home motion | G | Count-up on integers is decoration | 400 | Drop; a hover lift is CSS inside HOME |
| M05 Sign-in motion | V | A one-shot entrance for a polished sign-in page | 300 | Merge into R25 [120] |
| M06 Table, list, editor motion | G | FLIP, drag feedback, row flash on refetch | 700 | Drop |
| M07 Motion test harness | G | Frame-pacing and INP probes on a throttled CPU | 600 | Drop; one Q1 check that reduce-motion stops animations |
| M08 Pull to refresh | G | "Could"; the browser already offers it | 350 | Drop |
| Growth in 20 slices | G | 2,130 lines of per-slice motion detail | 2,130 | Replaced by the lean motion set below (about 800 lines inside F1, R08, R10, R13, R25) |

Lean motion set (all CSS or Web Animations, no library): press scale 0.97 and busy and Saved states on buttons; dialog pop and sheet slide; toast fade; route fade through view transitions; shimmer skeleton that stops after 6 sweeps; tab indicator; sign-in entrance and one shake on failure; stat-card hover lift on pointer devices. Everything honours `prefers-reduced-motion`. Hover colour stays instant. Not built: count-up, FLIP, drag feedback, chart draw-in, pull to refresh, shared-element transition, lite mode, in-app reduce switch.

### 2.6 Programme RP: Access screens (12 slices, 9,900 lines) and G: public-id flip (2 slices, 1,200 lines)

| Slice | Class | Reason | Lines | Action |
|---|---|---|---|---|
| RP00 Permission-derived profile helper | E | Stops new role-name constants; needed before R10 | 300 | Build [300] |
| RP01 Access foundations | E | Menu item, guards, catalog store | 800 | Build [450]: no bundled 21-code fallback, no `X-Permissions-Version` handling |
| RP02 Roles list | E | RQ5: add, retire, delete a role | 900 | Build [550]: plain table, Add role dialog (blank or copy of a role), retire, delete |
| RP03 Role editor core | E | RQ5: tick permissions per role | 1,100 | Build [1,000] with RP04's save step: grouped checklist, dependency auto-tick, High impact note, If-Match save, unsaved guard, one e2e scenario |
| RP04 Editor panels: access preview, changes, review and save | G | Preview sentences, diff with Undo, templates, impact preview dialog (needs RP-9) | 1,100 | Drop; copy-from-role replaces templates; the save toast says "3 added, 1 removed"; the audit holds the detail |
| RP05 Person's access page | E | RQ5: assign a role to a person | 1,150 | Build [350] inside the existing user form: role select, "Can work in every warehouse" switch; no new page |
| RP06 Exceptions with expiry | V | "Possibly" per-user exceptions; a copied role covers the odd person | 850 | Defer (section 6.1) |
| RP07 Safety rules and live rights change | V | The 409 messages and the own-page rule are in RP03 and RP05; the banner, header and 30-second polling are not needed | 850 | Merge messages [150] into RP03 and RP05; drop the banner |
| RP08 Compare roles and people | G | "Could" | 900 | Drop |
| RP09 History links, Activity tab | V | Needs B23; the Audit log screen already lists these events | 650 | Defer |
| RP10 Roles e2e | V | Tests belong to the slices | 900 | Merge: 150 lines in RP03 and in RP05 |
| RP11 Approval tier in Settings | E | A rule must name a tier once role names go away | 400 | Build [200] with RP05 |
| G01 Public-id web flip | E | RQ6 | 900 | Build [900] |
| G02 Id e2e fixes and merge | E | RQ6 | 300 | Build [300] |

### 2.7 Epics of the roadmap

| Epic | Class | Reason | Plan size | Action |
|---|---|---|---|---|
| EP01 Money and QA backend (#44 to #46) | E | RQ7 and bug fixes, already in flight | 16 h left | Finish as planned |
| EP02 Backend hardening and limits | E / V | ER-06, ER-07, ER-08, ER-14, ER-21 and SEC-08 are bugs and security; B1 to B3, B18, B20 are not | 27 h | Keep the bug and security items [12 h]; defer the rest |
| EP03 Document corrections | E | RQ4 | 30 h | Build recall and cancel on Approved [20 h, web included] |
| EP04 Public GUID ids (hybrid) | E | RQ6; the owner accepted the ADR | 85 h | Build, trimmed [74 h, web flip included] |
| EP05 UI refresh (R, A, Q, L) | mixed | 76 slices in these programmes, about a third essential | 365 h | Build 25 lean slices (R 18, F1, L 2, Q 1, A 3) |
| EP06 Roles backend (RP-0 to RP-8) | E | RQ5 | 167 h with review | Build, simplified [about 120 h] |
| EP07 Roles screens | E | RQ5 | 80 h | Build, simplified [22 h] |
| EP08 Motion language | V | RQ1, but 6,680 lines | 56 h | Replaced by the lean motion set (inside EP05 numbers) |
| EP09 Deployment | E | Nothing runs anywhere else yet | about 32 h | Unchanged; outside this review |
| EP10 Release gate (5 QA rounds, 7 gates) | V | The gates exist for a plan this size | 60 h | 4 QA rounds, 3 gates [30 h] |

### 2.8 Backend items (B-series, GUID PRs, roles ADR slices)

| Item | Class | Reason | Action |
|---|---|---|---|
| B1 `returned` filter, B2 ascending order, B3 `updatedAt` | V / G | HOME has fallbacks | Defer |
| B4 two-decimal rule on the server | E | RQ7; in PR #45 | Finish in #45 |
| B5 search verification | E | Verify only; in #46 | Finish in #46 |
| B6 reversal average cost (PR-01) | E | Bug; in #44 | Finish in #44 |
| B7 seek paging on stock, products, movements | G | Only virtual scroll needed it | Drop |
| B8 multi-sort | G | Only saved views needed it | Drop |
| B9 preference roaming | G | Needs an owner answer; no demand | Drop |
| B10 cancel and reverse actor and time | V | Timeline only | Defer |
| B11 bulk product lookup | G | Paste only | Drop |
| B12 daily movements, B13 stock value history (migration MG6), B14 low-stock by category | G | Charts only | Drop |
| B15 notification feed or ETag | G | Notifications panel only | Drop |
| B16 server CSV export | G | Duplicate of the Excel exports | Drop |
| B17 client-error endpoint | G | Console is enough for now | Drop |
| B18 administrator unlock | V | The lockout ends by itself; the banner works without it | Defer |
| B20 tighter text limits | V | L00 handles any length | Defer |
| B21 corrections domain, B22 corrections API | E | RQ4 | Build without send back |
| B23 audit filter by Role and User | V | RP09 only | Defer |
| GUID PR-0 to PR-10 | E | RQ6 | Build, trimmed (6.2) |
| GUID PR-7b idempotent create | G | Already optional (owner: D1 = no) | Stays dropped |
| `Partner.Code` and migration MG4 | V | Not required for non-guessable ids | Defer; ask the owner |
| Roles RP-0, RP-1, RP-2, RP-3, RP-4a, RP-4b, RP-5, RP-6, RP-8 | E | RQ5 | Build, simplified (6.1) |
| Roles RP-7 overrides | V | "Possibly" | Defer |
| Roles RP-9 impact preview and reset | G | Only the dropped review dialog needed it | Drop |

## 3. The lean plan

### 3.1 Frontend slices to build now (32 slices, about 17,600 lines)

Ids follow the plan; "F1" is the new merged foundation slice, "TB" the table toolbar slice. Dependencies are those of the plan unless stated.

| # | Slice | Contents | Lines | Lane | Needs |
|---|---|---|---|---|---|
| 1 | R02 | Required marker, legend, login rules | 550 | F | none |
| 2 | L00 | Content containment | 500 | O | none |
| 3 | R01 | Palette, aliases, contrast gate, no-flash theme script, budget 600/650 kB | 450 | Y | none |
| 4 | R27 | Quantity select-on-focus, double-save guard, NR-08/09 | 450 | F | R02 |
| 5 | R03 | Reports fixes | 350 | O | none |
| 6 | R33 | Ledger balance, one low-stock rule, search (client batch CS1) | 200 | T | PRs #44 to #46 merged |
| 7 | R04 | Money 2 decimals, quantity rule | 450 | F | R02 |
| 8 | F1 | Tokens, icons, lean motion tokens, reduced-motion contract, shimmer skeleton, delay helper, `@angular/animations` removed | 650 | Y | R01 |
| 9 | RP00 | Permission-derived profile helper | 300 | S | none |
| 10 | R08 | Buttons and form controls | 700 | F | R01, R02, F1 |
| 11 | R10 | Shell, tab bar, action bar, route fade, progress bar, tabs aria | 550 | S | F1, RP00 |
| 12 | Q1 | Stub harness (8 screens), axe, reduced-motion check, weekly WebKit | 600 | Q | none |
| 13 | R11 | One failure, one announcement | 650 | V | R08 |
| 14 | L02 | Layout frame and fixes (absorbs L01, L05) | 600 | O | R10 |
| 15 | A02 | Table: sticky header and first column, cards by container width, ER-10 labels | 650 | T | F1 |
| 16 | R13 | Dialog, sheet, toast, banner, empty-state look and motion | 600 | V | R11 |
| 17 | TB | Page size, First/Last, column show/hide | 600 | T | A02 |
| 18 | R25 | Sign-in page with entrance and shake | 750 | U | R01, R02, R08, R11 |
| 19 | R28 | Recall, Edit button, Post dialog (client batch CS2) | 600 | E | R11, R27, B22 |
| 20 | R31 | Cancel on Approved, shortage banner, NR-11 | 450 | E | R28 |
| 21 | G01 | Public-id web flip (CS3) | 900 | G | backend PR-7 on the integration branch |
| 22 | G02 | Id e2e fixes and merge | 300 | Q | G01 |
| 23 | R14+R15 | Saved state, row highlight, ER-03/04, skeleton adoption, ER-05 | 750 | E, T | R08, R11, F1 |
| 24 | HOME | Tappable tiles, returned drafts, restyled cards | 450 | H | G02 |
| 25 | A07 | Filters in the URL, active-filter chips | 500 | T | G02, A02, TB |
| 26 | R26 | Re-sign-in in place | 400 | E | R25, R11 |
| 27 | R29+R30 | Phone document screens, phone lists and wording | 750 | E, T | R28, R14+R15, designer ruling |
| 28 | RP01 | Roles menu, guards, catalog store | 450 | X | RP-3 contract, R10 |
| 29 | RP02 | Roles list, Add role, retire, delete | 550 | X | RP01, A02, RP-5 |
| 30 | RP03 | Role editor and its e2e | 1,000 | X | RP02, R14+R15, RP-6 |
| 31 | RP05+RP11 | Role and warehouse switch in the user form, tier in the Settings rule, e2e | 700 | X | RP03, RP-4b |
| 32 | R24 | Clean-up of the alias layer | 200 | Y | all |
| | **Total** | | **17,600** | | |

By programme: R 18 slices 9,300 lines; F1 650; L 2 slices 1,100; Q 1 slice 600; A 3 slices 1,750; G 2 slices 1,200; RP 5 slices 3,000.

### 3.2 Waves

At most 3 builds per wave; the roadmap runs 2 while both backend slots are busy. Waves 7, 8 and 13 to 15 are narrow on purpose: they follow backend readiness (corrections, ids, roles); the orchestrator fills a spare slot with fixes found by QA.

| Wave | Builds (lane, lines) | Lines | Gate |
|---|---|---|---|
| 1 | R02 (F, 550) / L00 (O, 500) / R01 (Y, 450) | 1,500 | merged, CI green |
| 2 | R27 (F, 450) / R03 (O, 350) / R33 (T, 200) | 1,000 | merged |
| 3 | R04 (F, 450) / F1 (Y, 650) / RP00 (S, 300) | 1,400 | M1; QA round 1 (one persona on a phone) |
| 4 | R08 (F, 700) / R10 (S, 550) / Q1 (Q, 600) | 1,850 | merged |
| 5 | R11 (V, 650) / L02 (O, 600) / A02 (T, 650) | 1,900 | merged |
| 6 | R13 (V, 600) / TB (T, 600) / R25 (U, 750) | 1,950 | M2; QA round 2; designer checks motion on one real low-end Android phone |
| 7 | R28 (E, 600) | 600 | merged (needs B22) |
| 8 | R31 (E, 450) | 450 | merged |
| 9 | G01 (G, 900), exclusive | 900 | lanes T and E frozen (about 1.5 days) |
| 10 | G02 (Q, 300) / R14+R15 (E, 750) | 1,050 | M3; IDOR retest by security; QA round 3 |
| 11 | HOME (H, 450) / A07 (T, 500) / R26 (E, 400) | 1,350 | merged |
| 12 | R29+R30 (E, 750) / RP01 (X, 450) | 1,200 | M4 |
| 13 | RP02 (X, 550) | 550 | merged |
| 14 | RP03 (X, 1,000) | 1,000 | merged |
| 15 | RP05+RP11 (X, 700) | 700 | M5; QA round 4 with the edge persona leading |
| 16 | R24 (Y, 200) | 200 | M6; final pass; one release of soak before this wave |

### 3.3 What the user sees at each milestone

| Milestone | After wave | What the user sees |
|---|---|---|
| M1 | 3 (about day 6) | The app in the calm new palette in light and dark; a red asterisk and "* means required"; money as `12,400.00` everywhere; typing in a quantity replaces the old value; one tap on Save saves once; no page scrolls sideways; the Reports date error is a sentence under the field; the ledger shows the balance after each entry |
| M2 | 6 (about day 12) | New buttons that press and show Saved; new top bar, tab bar and dialogs that open with a short pop or slide; pages fade between routes; tables with a sticky header and first column, First/Last and page size, and column show/hide; the new sign-in page with its entrance; a browser smoke with axe runs on every pull request |
| M3 | 10 (about day 20) | A wrong Submitted document is fixed with Edit, same number, same lines; a supervisor can cancel an Approved document with a reason; URLs and API ids are random GUIDs and an out-of-scope id answers 404; skeletons and a Saved state on the main screens; QA has retested the id switch |
| M4 | 12 (about day 25) | Home tiles open their list; returned drafts are listed; filters survive the back button and can be shared as links; the phone document and list screens are tidy; session expiry no longer loses a form |
| M5 | 15 (about day 33) | An administrator adds a role (blank or copied), ticks permissions, assigns it to a person, and sees the audit entry; the last administrator cannot be removed; a demotion takes effect within seconds; approval rules name a tier |
| M6 | 16 (about day 38) | The compatibility layer is gone; final QA pass; ready for the deployment epic |

### 3.4 Backend track (runs beside the waves)

| Step | Content | Hours |
|---|---|---|
| P0 | Merge PRs #44, #45, #46 (EP01); the backend bug and security items of EP02 | 16 + 12 |
| Corrections | B21, B22 lean (recall, cancel on Approved), before the roles oracle fixtures | 12 |
| Ids | GUID PR-0 to PR-3 on `main`, PR-4 to PR-8 on `feature/public-ids`, PR-9 merge, PR-10 | 74 |
| Roles on `main` early | RP-0, RP-1, RP-2 (no wire change) | 29 |
| Roles after PR-9 | RP-3, RP-4a, RP-4b, RP-5, RP-6, RP-8 | 70 |

### 3.5 Lean definition of done (replaces the 18-row list)

1. Tests of the slice pass; lint, format, contrast and the production build pass; the initial bundle stays under 600 kB.
2. The existing 13 real-API scenarios and the existing Jest specs stay green (nothing built is rewritten without a test that proves the same behaviour).
3. No cost or value reaches a user without `cost.view` / `valuation.view` (rule D5), on every new surface.
4. Looked at once at 360 and 1280 wide, light and dark, with reduced motion; no horizontal page scroll (L00 rule).
5. en, zh, ms keys present (zh and ms stay `TODO-review`).
6. Independent review before merge (the owner's decision N14), using the checklist of section 1.
7. The slice states which stated requirement it serves.

## 4. Deferred: kept in the plan, built only when the trigger fires

A deferred slice keeps its id and its design on disk. It is built only when the trigger is recorded in `docs/team/HANDOFFS.md` by the project-manager.

| Item (plan size) | Trigger |
|---|---|
| R12 Density (500) | Two users on large screens ask to see more rows |
| R16, R18, R20 Command palette (1,950) | Users report they cannot find screens, or the menu passes 12 entries |
| R19 Reviewer, Overseer, Admin Home variants (900) with B1 to B3 | Approvers say the tiles are not enough, or a user has more than 25 drafts and a returned one is missing |
| R23 Date field dd/MM/yyyy (600) | Built (PRs #96, #98, #100); no trigger left |
| A03 resize and reorder (about 700) | Users ask to change column widths or order |
| A04 Keyboard grid navigation (800) | An accessibility audit requires it, or power users ask |
| A06 Saved views and multi-sort (900, with B8) | The same filter is rebuilt by hand every day by the same users |
| A08 Row expansion (620) | Approvers complain about opening every document to see its lines |
| A09 Bulk selection (840) | A read-only bulk need appears (for example "create a receipt from low-stock rows") |
| A12, A13 Combobox (1,340) | QA finds the product picker unusable by keyboard or scanner |
| A16 Side drawer (920) | Supervisors approve more than about 20 documents a day |
| A17 Timeline (640, with B10) | Users ask who cancelled or reversed a document |
| A18 Notifications panel (760) | Waiting approvals are missed for more than a day |
| L03 Collapsible rail (1,000) | Users ask for more content width on desktop |
| L06 Forced colours (600) | A customer uses Windows high contrast, or an audit requires it |
| L07 Print stylesheets (700) | Users print receipts or issues |
| Q05 Global error handling (750) | Unhandled errors reach QA or users twice in a release |
| Q06 Offline banner and read cache (800) | A warehouse reports Wi-Fi loss |
| RP06 Per-user exceptions (850, backend RP-7) | An administrator needs a one-person exception that a copied role would make awkward (more than about three such roles) |
| RP09 History links (650, with B23) | Administrators ask to see role changes from the role page |
| Send back on Approved (part of R31, B21) | More than about five cancel-and-retype cases a month, or a complaint |
| B18 Administrator unlock (about 150 lines) | The first support call about a locked account |
| B20 Tighter text limits | Long names appear in real data and L00 containment is not enough |
| `Partner.Code` and migration MG4 (3 h) | A customer needs partner codes on documents or imports |
| Per-user preference store (from R06, 560) | The first feature that needs per-user settings (density, saved views) is approved |
| Server-side epoch cache for access snapshots | A second API instance is deployed |

## 5. Dropped, with reasons

| Dropped | Lines | Why |
|---|---|---|
| A01 CDK, `@angular/cdk` dependency | 650 | Native `<dialog>`, `popover` and CSS cover the few overlays we keep; a dependency plus an overlay-in-dialog workaround for none of the kept features |
| A10 CSV export, B16 | 600 | Duplicates the four server Excel exports and creates a second path that must obey cost rules |
| A11 Virtual scroll, B7 | 1,000 | A page is at most 200 rows |
| A14, A15 Lines grid and paste, B11 | 2,000 | Spreadsheet entry for phone users |
| A19, A20 Charts, A21 illustrations, A22 breadcrumbs; B12 to B14, migration MG6 | 3,150 | Not requested; charts need a nightly snapshot and a migration |
| M02, M04, M06, M07, M08 and the 2,130 lines of per-slice motion growth | 5,080 | Count-up, FLIP, drag feedback, pull to refresh, harnesses and four motion modes for an owner who asked for "animation" |
| Q02 Visual baselines on an orphan branch | 700 | A workflow and a data branch to guard a risk that has not occurred |
| Q03 Three-engine PR matrix | 600 | Firefox users are not our users; WebKit stands in for iPhones and runs weekly |
| Q07 Lint slice, Q08, Q09 Contract tests and `jest-axe`, Q10 Gallery, Q11 CI wiring, Q12 Lighthouse and `@lhci/cli` | 4,120 | New tooling for gates we do not build; each slice writes its own specs |
| L08 Matrix automation, L09 Large screens | 1,300 | Replaced by Q1 at three widths; speculative devices |
| R22 Keyboard shortcuts, R32 e2e slice | 1,000 | Could item; tests move into the slices |
| RP04 Access preview, changes diff, templates, impact preview (and backend RP-9) | 1,100 + 8 h | The copy-from-role action replaces templates; the server returns added and removed for the audit |
| RP07 "Your access was changed" banner, `X-Permissions-Version`, 30-second polling | 700 | The server enforces within seconds; the client re-reads `/api/auth/me` on a 403 and at token refresh |
| RP08 Compare views | 900 | "Could" |
| B7, B8, B9, B11, B15, B17, B19 | | Each existed for a dropped feature |
| Plan gates G1 to G7, the 25-wave quality net, 5 QA rounds | | Replaced by 4 rounds and 3 gates |

## 6. Simplified designs

### 6.1 Roles and permissions

What is built. Permissions are defined in code (the 21 codes of the ADR, 6 of them new), served at `GET /api/permissions`; there is no `Permission` table and no start-up sync: a release that adds a permission ships a migration that grants it to Admin and the right defaults, and a test fails if a catalog code is not held by Admin on a fresh database. Roles live in the database (`Role`, `RolePermission` with code strings); five system roles are seeded with exactly today's permissions; custom roles can be added (blank or copied), renamed, retired and deleted when no one holds them. One role per person. `User.AllWarehouses` replaces the Admin and Owner special case; `ApprovalRule` stores a tier, and the two tiers are the permissions `document.approve` and `document.approve.large`. This is the cheapest faithful way to remove role names from approval routing: it keeps today's rule (Owner reviews large only, Supervisor the rest, Admin both) and its 44 tests. Safety rails stay because they are what the owner named: Admin keeps the four administrability codes, the last holder cannot be removed (one application lock, closing the real race that exists today), nobody edits their own role, nobody grants more than they hold, dangerous permissions need an administrator and a reason, closure under "requires", `If-Match` on the permission replace, every change audited (row level plus explicit events). Freshness (ER-15, SEC-06): the token carries identity only; each request reads an access snapshot from `IMemoryCache` with a 15-second time to live that is evicted on writes; a deactivated user gets 401; rights change within 15 seconds instead of 15 minutes. The rights-parity test against oracle fixtures of today's behaviour is kept: it is the proof that nothing built changed.

What is not built, and the lean alternative. The access epoch table, the 2-second watcher, `X-Permissions-Version` and the two-host tests (one API instance is the hosting default; add the epoch when a second instance exists); the `Permission` table and sync; per-user exceptions (deferred: a copied role covers the odd person, and the design stays open because `EffectivePermissions` already takes grants as an input set; when built, a grant needs a reason and an optional end date checked at read time, with no sweeper job, about 500 backend and 500 web lines and 8 h); templates, "Reset to standard", impact preview, access preview, diff view, compare view, history tab, live banner.

Web: Roles menu gated by `roles.read`, a plain list with Add role, one editor page with a grouped checklist (ticking a permission ticks what it requires and says so), the role select and warehouse switch in the existing user form, the tier select in the Settings rule form. The web never compares a role name.

Effect. Backend 138 h to about 99 h (about 120 h with review), 10,200 to about 7,000 lines; web 9,900 to 3,000 lines (80 h to 22 h).

### 6.2 Non-guessable ids

The owner accepted the hybrid, and it is already close to the minimum: integer clustered keys stay, a random version-4 `PublicId` on the 9 tables, 404 (never 403) for ids outside the caller's scope, opaque cursors, no row id on ledger and audit responses. Honest note: the protection against reading someone else's data is the 404 rule (SEC-11, about 80 lines, build it first); the GUID removes enumeration and counting. Both together are what RQ6 means.

Is the integration branch the simplest way? Yes. The API and the web must flip together, the repository has no production data, and the alternative (the API accepting both integer and GUID ids for a while) creates a second code path in every handler. Keep one branch, with no migration on it (schema first on `main`). Are opaque cursors the simplest? The web does not read cursors; they matter because `nextCursor` currently carries a numeric id. Protecting the cursor with ASP.NET Data Protection is about 100 lines and a unit test; plain base64 would be decodable. Keep.

What is trimmed. PR-1 safety net from 650 lines to about 300 (paging walks and the out-of-scope 404 matrix; drop the whole-schema pin); PR-2b to the minimum that keeps k6 and e2e working; PR-10 from a full 1M-tier re-measure and QA smoke to an IDOR retest by security plus one by-id timing check; `Partner.Code` and MG4 deferred; PR-7b stays dropped. Do the flip before the roles endpoints are written so they use string ids from the first line (saves the 3 h of rework the roles ADR warns about). Hours: 85 to about 74. Per-table savings by skipping Category, Uom and ApprovalRule would be about 6 h but would leave two id types on the wire; not recommended.

### 6.3 Document corrections

Recall plus cancel is enough. What is built: recall (Edit) by the submitter while the document is Submitted, back to Draft with the same number and lines, no reason asked, one short confirm only if the document is waiting for approval; the Post dialog becomes [Edit items] [Post]; Cancel on an Approved document by a reviewer with a reason of at least 5 characters (closes PR-02); Reject on Submitted already exists. Approved documents are rare and the approver already has an eye on the numbers, so "cancel with a reason and retype" is an acceptable cost until the trigger in section 4 fires. What is not built: send back (Approved to a Returned draft, `WithdrawApproval`, `document.approval-withdrawn`), the separate e2e slice (one scenario file covers both). No new columns.

Effect. Backend B21 and B22 from about 1,250 to about 900 lines (17 h to 12 h); web 1,900 to 1,050 lines.

### 6.4 Effect on agent-hours (roadmap basis, review included)

| Epic | Plan | Lean |
|---|---|---|
| EP01 Money and QA backend | 16 | 16 |
| EP02 Hardening (bugs and security only) | 27 | 12 |
| EP03 Corrections (backend and web) | 30 | 20 |
| EP04 Public ids (backend and web flip) | 85 | 74 |
| EP05 + EP08 Refresh and motion (web, without the slices counted elsewhere) | 421 | 90 |
| EP06 Roles backend (with review and QA) | 167 | 120 |
| EP07 Roles screens | 80 | 22 |
| EP09 Deployment (unchanged) | 32 | 30 |
| EP10 Release gate | 60 | 30 |
| **Total** | **about 915** | **about 415** |

Web hours use the roadmap's own rate (about 135 changed lines per agent-hour all in). With the 25% rework allowance: 520 hours.

## 7. Risks of going lean

| Risk | Likelihood | What it would cost | Mitigation |
|---|---|---|---|
| The native table cannot take the grid keyboard model or virtual scroll later | Low to medium | Rewrite of `app-table` internals (the plan's A02 size, about 1,000 lines) | The table keeps its public inputs; the rewrite is possible later behind them. Trigger in section 4 |
| An accessibility audit asks for the items deferred (grid navigation, forced colours) | Medium | A04 and L06, about 1,400 lines, 10 h | Specs stay on disk; keyboard access to links, buttons and sorting works today |
| Per-user exceptions are wanted after all | Medium | About 20 h in total (500 + 500 lines, one migration) | `EffectivePermissions` takes grants as an input set; the ADR section 2.5 rules apply |
| A second API instance is deployed | Low | Epoch table and watcher, about 150 lines and 3 h; until then rights can lag by one cache time to live | Port `IAccessSnapshots` is kept |
| A release adds a permission and forgets the migration | Low to medium | Admin lacks the new right until fixed | The catalog test on a fresh database fails the build |
| Visual regressions reach QA because there are no baselines | Medium | A fix round per release, no data risk | Screenshots are uploaded by Q1 for the designer; add Q02 when two regressions slip |
| A Safari-only layout bug appears (container queries, view transitions) | Medium | One fix round | Weekly WebKit run; all motion has a no-motion fallback |
| An owner request for charts, saved views or bulk actions arrives | Medium | The slices exist on disk with sizes; none has a dependency that blocks it, except charts (migration MG6, B12 to B14) | Treat as a new request through the project-manager |
| Without the density and preference store, the first per-user feature needs a small store first | Medium | About 560 lines before that feature | The design exists in R06 |
| Someone builds a deferred slice because its spec is still in the repository | Medium | Wasted effort | This file is the scope; the plan header gets a pointer to it when the owner approves; the checklist in section 1 |
| Cutting the stub-based tests of Q03, Q08, Q09 leaves component regressions to per-slice specs | Low | Occasional late find | Each slice keeps its specs; Q1 adds axe on key screens |
| The backend trims (no epoch cache, no Permission table) hide a design flaw until scale | Low | Rework in a backend module that already sits behind a port | Parity oracle plus the generated endpoint matrix (RP-8) |

## 8. Questions for the owner

Defaults apply if no answer comes.

1. Roles first, per-user exceptions later? Recommended yes. A copied role covers a one-off person; exceptions add a table, three endpoints, a form and a rule set. Say "build exceptions now" if you want them in the first release (+20 hours).
2. Is "more advanced tables" met by: sticky header and first column, cards on narrow screens, page size with First/Last, show/hide columns, and filters that live in the URL? Resize, reorder, saved views, bulk actions and endless scrolling are deferred.
3. Is the lean motion set (section 2.5) enough "animation"? It leaves out count-up numbers, pull to refresh, shared-element transitions and an in-app "reduce animations" switch.
4. One earlier "yes" default I recommend turning into "not now": the optional `Partner.Code`. (The `dd/MM/yyyy` date field, R23, was built after all.)
5. Confirm the roles defaults already in the roadmap (O2): one role per person, two approval levels, a role in use cannot be retired, cost visibility changeable by administrators with a reason and an audit entry.
