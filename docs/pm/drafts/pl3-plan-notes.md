# PL3 plan: working notes (NOT a finished plan; no plan file was written to the repo)

Read so far: pilot-slice-2-plan.md, ROADMAP.md, go-live-checklist.md, ADR modules, backend.md, TASKS.md. PRD is not in the repo (docs/private absent); ids from TASKS: OUT-01 (Sale), OUT-04 (delivery note print).

## Recommendation
- Build returns FIRST (stage A, independent of any sales order, links to a Posted Sale issue), then sales order + delivery (stage B, a copy of PL2 patterns), price lists and supplier returns last and optional (stage C, cut first).
- Gate G1 after stage A: owner confirms sales orders are wanted. Fallback "delivery note + returns only" is about 5,700 lines / 7 days; additive, SO can be added later.

## Calibration (PL2 actuals, hand-written, generated client and designers excluded)
Backend PRs: 759, 977, 1495, 1421, 1004, 1459, 588, 936, 939. Web PRs: 1488, 1296, 1128, 1464, 946, 1416, 1481. PL2 total 19,900 vs 8,600 planned = 2.3x (backend about 2.1x, web about 2.6x).
Multiplier applied to PL3: about 2.3 overall (copy parts 1.9 backend / 2.3 web, new design parts 2.4 / 2.6): tests are about 70% of backend lines and every web screen needs specs and an e2e; only first-time discovery shrinks. Days stay on the method (planned lines at 70 / 135 lines per hour, 6 h/day, x1.25), as PL2 did.

## Sub-slices (planned -> expected built lines; days with rework)
Stage A returns: PL3.1 module `returns` (no permission, no migration if tests allow) 150 -> 300, 0.5d Light; PL3.2 return link, create from Posted Sale issue, save rules, MG14 600 -> 1,450, 2.0d Full; PL3.3 posting a return, `ret:{src}` lock, over-return check, source-reversal guard 450 -> 1,100, 1.5d Full; PL3.4 web return 500 -> 1,300, 1.0d Full; PL3.5 delivery note print (OUT-04, no prices) 150 -> 350, 0.5d Light. Stage A: 4,500 lines, 5.5d.
Stage B sales: PL3.6 module `sales`, so.read/so.manage, MG15 grants 500 -> 850, 1.5d Full; PL3.7a SO domain + tables 450 -> 900, 1.5d Full; PL3.7b SO endpoints, numbering, client 550 -> 1,300, 1.5d Full; PL3.8a workflow, DocumentType.SalesOrder=6 600 -> 1,150, 2.0d Full; PL3.8b CanDisable, rule seed, enable guard 300 -> 700, 1.0d Full; PL3.9 delivery link, create from SO, MG18 650 -> 1,250, 2.0d Full; PL3.10 delivered/remaining, cancel guard 300 -> 550, 1.0d Full; PL3.11 posting linked delivery, `so` lock, over-delivery, auto-close, close short 500 -> 900, 1.5d Full. Web: list/detail/actions 3 PRs 800 -> 3,000, 1.5d Full; editor + print 2 PRs 900 -> 2,000, 1.5d Standard; deliver against SO 700 -> 1,500, 1.0d Full.
Closing PL3.12: story, security retest, DBA 1M, backend.md, go-live 400 -> 1,200, 1.0d.
Stage C optional: supplier return (Issue linked to a Posted receipt, new IssuePurpose value, partner rule flip) 600 -> 1,450, 1.5d; price lists (one table, partner null = default) 1,200 -> 2,900, 3.0d.
Totals: core 19,800 lines (planned about 8,500), 22.5 agent-days; with options 24,150 lines, 27.0 days.

## Decisions
- Modules `sales` and `returns`; `Requires` enforcement still NOT built (returns has no prerequisite until lots exist; contradicts ROADMAP/PL2 plan text that said returns triggers it). Record in HANDOFFS.
- No new roles. Permissions so.read / so.manage like po.*; returns reuses document.* (fallback returns.create if a catalog test forces one). Price lists, if built: pricelist.manage or masterdata write.
- Selling prices tagged [VisibleWith(cost.view)]; Warehouse staff see quantities only; delivery note shows no prices.
- Return = NEW document (customer return = Receipt with ReturnOfDocumentId + line ReturnOfLineNo; supplier return = Issue), not a reversal. Reversal stays for mistakes and is refused while a live return exists. Returned qty computed from Posted returns only; a Closed SO is never reopened.
- Return cost = source line UnitCost exactly, via the existing SetKnownCost / ApplyKnownReceiptCosts path (typed cost is limited to 2 decimals per entered unit). Nothing is refunded in money: no credit note, no refund amount.
- Partner.EnsureUsableFor needs a return variant (Receipt takes a Customer, Issue a Supplier).
- Returns always need a Standard (Supervisor) approval; reason (remark, 5+ chars) required; same warehouse as the source; damaged goods are scrapped afterwards.
- Lock order: modules(S) -> one of so / po / ret (never together) -> docseq -> document -> stock ascending. Third reader copy (delivered) triggers extracting a shared linked-quantity reader (budget about 150 lines in PL3.11).
- Migrations one at a time: MG14 AddReturnLinks, MG15 GrantSalesPermissions, MG16 AddSalesOrders, MG17 AddSalesOrderApprovalRules (data), MG18 AddDeliverySalesOrderLinks, MG19 price list (optional). Chain head on main: RemoveTaxReadyFields (20261022141734).
- e2e: 45-returns, 46-delivery-note, 47-sales-orders, 48-deliver-against-so (optional 49 supplier return, 50 price lists). Security probes in tests/security/pl3-sales/.

## Owner questions (defaults)
1. Sales order then delivery, or delivery note only? Default SO + delivery, built after returns, gate G1. Wrong choice costs about 14,100 lines / 16 days if SO is built and unwanted; nothing lost if deferred (additive).
2. SO approval: same two tiers, placeholders RM 5,000 / RM 20,000; delivery keeps the existing issue rule (double approval accepted as PL2 risk 11). Cut option: no SO approval, about 1,800 lines / 3 days.
3. Price lists: not in core; typed price per SO line; build only on a yes (customers with agreed prices). Changes the ROADMAP 8.4 default (second PL3 slice).
4. Returns rules: approve always (Supervisor), any age, source cost, same warehouse, scrap afterwards.
5. Supplier returns: yes, last, cut if the pilot does not return goods to suppliers.

## Still to write
The real plan file docs/pm/pilot-slice-3-plan.md (template: pilot-slice-2-plan.md), the short PL3 pointer in TASKS.md (line 121), a HANDOFFS entry, and the PR.
