# QA retest: power supervisor (desktop), after the fix round

Persona: busy warehouse supervisor at a desk. Date of run: 2026-10-11. Build: current main, web http://localhost:4200 (production build), API http://localhost:5109, fresh throw-away database. Previous report: `docs/qa/power-supervisor.md` (PS-01..PS-26). Fix status read first: `docs/qa/fix-status-backend.md`, `docs/qa/fix-status-frontend.md`, `docs/team/HANDOFFS.md` (contract changes), `docs/design/mvp-ux.md` (1.4, 3.9, approval, reversal).
Roles used: supervisor (main, in the browser), staff, clerk, owner, admin (browser sessions for the permission comparison and the Owner approval, API for the rest). Credentials are not recorded here.

Result: 0 Blocker, 1 Major, 7 Minor, 6 Polish new findings. Of the 26 old findings: 18 fixed, 5 still open, 2 need a product decision, 1 not reproducible, 0 regressed. Every business flow works end to end except one valuation defect: reversing a receipt does not take its cost back out of the average (PR-01), so the stock value and later issue estimates stay wrong.

## Method and limits

- Browser: one pane tab (`seed`), 1280x720 emulation. Sign-ins by keyboard (Tab, Enter). Network tab and console read after each flow. The pane crops or times out screenshots now and then; I used the accessibility tree and page text as the main evidence and screenshots only for visual checks.
- Data: seeded through the public API (5 QA products, 700 bulk products, 335 posted receipts, 9 issues, 1 reversed receipt, 1 reversed issue, one negative balance) so that lists page and the numbers can be reconciled by script against the API.
- "Second tab": the pane has no second tab, so another person's change was made through the API while the first document was open in the UI (same effect on the token).
- Excel: I did not press Export Excel in the browser (it would save a file on this machine). The four export endpoints were fetched through the API as supervisor and read cell by cell. The request the UI builds (current filters) was not observed.
- Not testable: the product import wizard upload step (no file picker in the pane; import was run through the API), the KL day boundary of the date filter (no document exists between 00:00 and 08:00 KL today), a real second browser.
- Sign-in budget: about 20 reloads and 6 sign-ins in the session; no 429 seen.

## Old findings PS-01..PS-26

| ID | Old severity | Status | Evidence in this run |
|---|---|---|---|
| PS-01 reversal blocked wrong message | Major | fixed | Reverse of RCV (DEMO-001, QA-001, QA-002) after later issues: banner "Can't reverse RCV-... It would make DEMO-001 (-3 PCS), QA-002 (-500 PCS) negative. Reverse or adjust the later issues first, or ask an Admin." Both short products named, no stale banner, no toast. Residual (known, FE status "not done"): the dialog closes and the typed reason is lost. |
| PS-02 import creates category/unit, wrong number text | Major | fixed | API preview of a 9-row file: unknown category and unit are row errors, "Safety stock 'abc' is not a number.", "SKU 'PR-IMP-001' also appears in row 3.", commit with errors answers 400 and creates nothing (category and unit lists unchanged). Name match of unit and category is case-insensitive. UI upload not tested. |
| PS-03 value that triggers approval invisible, issues show 0.00 | Major | fixed | Inbox has an Amount column ("5,000.00 est."), document page shows Amount "est." and "Why this needs approval: The value 5,000.00 reaches the 5,000.00 limit. Approver: Supervisor." Receipt: "The quantity 2,505 reaches the 1,000 limit." |
| PS-04 never-stocked and negative not flagged | Major | fixed | QA-003 (safety 5, never received) is in Low stock, Home count and badge. After an override issue QA-004 shows -15 with a "Negative" chip on Low stock and Stock. Home 62 = list total 62 = export rows 62 (later 64 = 64 after two imported products). |
| PS-05 Add item search not focused | Major | fixed | Click Add item, type barcode `BC-QA-002`, Enter: "QA-002 added." with no click into the box (also with SKU). Same on the issue form. |
| PS-06 audit filters exact | Major | fixed | Search box with a document number finds the 5 rows of that document; "doc" finds StockDocument (API); empty search shows "Clear filters"; Action dropdown from `/api/audit/filters`. Residuals: no entity-type dropdown, suffix search ("0002") finds nothing (prefix only), see PR-12. |
| PS-07 `allowedActions` offers Approve that fails | Major | fixed | Large receipt (120,000, Owner rule): supervisor `allowedActions` = cancel only, Owner = approve, reject, Admin = cancel, approve, reject; supervisor approve answers 403 "above your approval limit"; list items carry `canAct` (supervisor false, owner true); Home badge equals the inbox in both roles; Owner approve on a non-large document 403. |
| PS-08 stale banner wrong twice | Major | fixed | A: document changed by staff while open, then Approve: banner (with name and time when the 409 arrives: "Staff changed this document at 11 Oct 2026, 08:51"), buttons locked, Reload clears it and shows the Cancelled document. B: reversing ISS-WH01-2610-0002 in the UI: no banner at all (waited 5 s). Draft edit: other user's save, then Reload shows their version. Residual: PR-09. |
| PS-09 short reasons accepted | Major | fixed | API: reject "no", reverse "x", override "abc", reject "ab    " all 400 "Enter a reason of at least 5 characters."; blank 400 "required". UI shows the same sentence in the dialog. |
| PS-10 submitter can reject own document | Minor | fixed | Self reject 400 "You cannot reject a document you submitted." while self-approval is off; UI hides Reject and shows a disabled Approve with the E3 sentence. With the setting on, both Approve and Reject appear and self-approval works. |
| PS-11 approve of an issue that is already short | Minor | still open | The line says "Short by 2,000 (on hand 0)" in red, but the Approve dialog still says only "The document can then be posted." Approved, then Post fails and nobody can cancel (see PR-02). |
| PS-12 audit content | Minor | still open (partly) | Details now show enum names (`"Status": "Approved"`), settings rows show old and new value as plain `"Value":"false"` to `"true"`. A settings save still writes one row per changed key plus `settings.updated`. Sign-ins can only be hidden by choosing one other action; there is no "everything except sign-ins". |
| PS-13 paging only Previous/Next | Minor | needs product decision | 14 pages of receipts, still only Previous, Next and "Page 1 of 14". FE status: waits for a design decision. API timings are fine (see Numbers). |
| PS-14 ledger without balance after | Minor | still open | Product ledger columns: document, date, type, warehouse, change, user. Backend decision "not done" (append-only table). Reversal rows now carry the type "Reversal". |
| PS-15 reports date format, empty state, signed quantity, export message | Minor | still open (partly) | Fixed: signed Stock effect, Export stays visible on "No data for these filters". Still open: date inputs show mm/dd/yyyy (needs the date-format decision), empty state has no action, no message after Export (not pressed, see limits). New: From after To shows "Something went wrong" (PR-03). |
| PS-16 Home counts | Minor | fixed | Posted today 335 = Posted list 335 (the reversed document is excluded); Products with stock 303 = rows above zero (303 counted by script); Waiting 0/1/2 = inbox = badge. |
| PS-17 approver wording | Minor | fixed | Reject dialog "It goes back to Staff as a draft with your reason.", toast "Returned to Staff for changes.", "Submitted by" on the page, override dialog "QA-004 will go to -30 PCS." Residual: the inbox row is still the only link (no Review button). |
| PS-18 rule boundary wording | Minor | fixed | Page text says "reaches"; the design doc uses "reaches". |
| PS-19 import template barcodes | Minor | fixed | The unchanged template row previews as New with 0 errors (barcodes `SAMPLE-...`). |
| PS-20 header "New receipt" while loading | Polish | not reproducible | The tab title and the first snapshots while loading show "IMS" and an empty body; no stale "New receipt" heading observed. |
| PS-21 two clear icons | Polish | fixed | One "Clear" button in the Add item search and the stock search. |
| PS-22 SKU and numbers wrap | Polish | still open | Stock list: no wrap. Reports at 1280x720: SKU wraps at the hyphen ("QA-" / "002", "BULK-" / "0300") and the posted date wraps over three lines (PR-06). |
| PS-23 "Set to 0" offered | Polish | fixed | Line with 0 available shows "Only 0 PCS available in Main warehouse." and no Set to button. |
| PS-24 user dialog checkbox names, cool-down message | Polish | fixed | Cool-down 9999: "Enter 720 or less." The checkbox is wrapped by its label in `checkbox.component.html`, so the name is the label text (the accessibility tree of the pane prints the value "on", that is the tool, not the page). |
| PS-25 unquoted If-Match ignored | Polish | fixed | Bare wrong token 409, bare right token 200, weak `W/"x"` 409, missing header 428 `if_match_required`. The web app sends the token on every command (Submit after Reject = PUT then POST, both 200). |
| PS-26 confirm dialog focus and double click | Polish | needs product decision | Dialog still opens with focus on the container (one extra Tab), by design for scanners (FE status). Esc closes and returns focus to the Approve button (Enter reopens it). Keyboard confirm = Tab, Tab, Tab, Enter on Approve and Tab, Tab, Enter on Post; works. |

Counts: fixed 18 (PS-01..10, 16..19, 21, 23, 24, 25), still open 5 (PS-11, 12, 14, 15, 22), needs product decision 2 (PS-13, 26), not reproducible 1 (PS-20), regressed 0.

## The 17 business flows (second round)

| # | Flow | Result |
|---|---|---|
| 1 | Clerk submits a large receipt (qty rule 1,000): Approvals shows it with Amount, supervisor approves, posts, stock and ledger update; Owner approves a 120,000 receipt in the UI (keyboard only), supervisor posts | Pass |
| 2 | Staff issue below zero with negative stock ON: Submitted + Override; override reason asked in the Approve dialog (5 characters, "abc" refused, then full reason), Post goes to -30, History shows "Negative stock override: Reason: ...", audit has it | Pass |
| 3 | Negative OFF, issue above stock: inline "Only 2 PCS available in Main warehouse. Set to 2", footer "1 line is short on stock. Fix it to submit." | Pass |
| 4 | Approvals inbox: Amount column with "est.", Override flag, Large flag for the Owner, counts equal Home and badge, large Owner document absent from the supervisor inbox, own document appears only when self-approval is on | Pass |
| 5 | Reject with reason: creator sees Draft + Returned + the reason, resubmit clears the flag, counts `returnedToMe` 1 then 0 | Pass |
| 6 | Self-approval blocked (UI disabled Approve with sentence, API 400 both approve and reject); setting on lets approve, post work | Pass |
| 7 | Reversal with reason: ledger gets an opposite row, document Reversed, no false banner; blocked case names every product (PS-01). After reversing a receipt the average cost stays contaminated | Fail (PR-01) |
| 8 | Stock list: search by barcode, Low stock only, category, Negative chip, product ledger, paging; Stock value and rows reconcile with the summary | Pass with PR-04, PR-13 |
| 9 | Movements report: Type and date filters, paging 25 + 15, order newest first, signed Stock effect, Reversal badge, totals; export columns | Pass with PR-03, PR-06, PR-08 |
| 10 | Low stock: list, Home tile, badge, export (63 rows = 62 + header) | Pass |
| 11 | Audit: search by document number, action dropdown, empty state with Clear filters, enum names in details, Owner and Admin only (supervisor 403) | Pass |
| 12 | Stale document, two actors: detected before and at the click, explained, buttons locked, Reload clears | Pass with PR-09 |
| 13 | Product import preview and commit through the API (unknown category, unit, bad number, duplicate, empty SKU, barcode clash, all-or-nothing) | Pass (API only; UI upload not testable) |
| 14 | Settings: self-approval toggle saved in the UI and effective at once; cool-down max message; audit rows written | Pass |
| 15 | Users as admin: list, edit dialog, create 200, duplicate 409, 12-character rule, bad characters, unknown warehouse 400, self demote and self deactivate 409, deactivate other 200; supervisor 403 on the user list | Pass |
| 16 | Large lists: 700 products, 335 posted receipts, 1,872 audit rows. API 4 to 105 ms warm, first calls up to 105 ms; exports 40 to 83 ms | Pass for speed; paging controls unchanged (PS-13) |
| 17 | Keyboard: sign-in, Esc closes dialogs and returns focus, Tab confirm of Approve and Post, scanner path in Add item (focus works) | Pass |

## New findings, sorted by severity

### Major

**PR-01 Major, Reversing a receipt does not restore the average cost: stock value and later issue estimates stay wrong**
- Steps: QA-002 holds 735 PCS (average about 4.76, value about 3,495). A clerk submits a receipt of 600 x 200.00 (RCV-WH01-2610-0033, Owner rule), the Owner approves, supervisor posts: QA-002 becomes 1,335 PCS at an average of 92.51. Supervisor reverses the receipt with a reason ("Duplicate delivery entry"). Open Stock, Home and the summary. Then have staff submit an issue of 60 QA-002.
- Expected: after the reversal the product is back to 735 PCS at the old average (about 4.76, value about 3,495), the Home Stock value is back to what it was before the receipt (54,875.50), and the issue of 60 is valued at about 285.
- Actual: quantity is back to 735 but the average stays 92.505992 and the value is 67,991.90. Home "Stock value" and `/api/stock/summary` `totalValue` went from 54,875.50 to 119,371.90 (about 64,496 too high). The staff issue of 60 PCS (really about 285) is estimated at 5,550.36 and is sent for approval by the 5,000 rule ("The value 5,550.36 reaches the 5,000.00 limit"). Cause in code: `ReceiptPostingStrategy.Reverse` only calls `balance.Issue(...)` (average unchanged); the ledger row of the reversal carries the receipt cost (200) but the balance keeps the blended average. An issue reversal (receive back at issue cost) leaves the average unchanged and is fine.
- Suggested fix: when a receipt is reversed, remove the quantity at its receipt cost from the average (newAvg = (qty x avg - baseQty x cost) / (qty - baseQty), keep the average when the remaining quantity is 0 or less), or recompute the average from the ledger; add a test with a receipt at a very different cost followed by its reversal and an issue valuation. Owner: be-framework.

### Minor

**PR-02 Minor, An approved issue that cannot be posted is a dead end, and the advice is wrong for a supervisor** (residual of PS-11 and design question Q3)
- Steps: negative stock OFF. Staff issue of 2,000 QA-001 (5,000, needs approval) is waiting; supervisor posts another issue of 2,000 so on hand is 0. Open the waiting issue: line shows "Short by 2,000 (on hand 0)". Approve (dialog says only "The document can then be posted."), then Post.
- Expected: a warning at approval that it cannot be posted now, and a way out for the supervisor (Cancel on an Approved document, UX Q3).
- Actual: Post answers 409 and the page shows "Stock changed. Stock for QA-001 is now 0 PCS, but the document needs 2,000. Ask a supervisor for help." (the reader is the supervisor). `allowedActions` for supervisor and Admin is only `post`; Admin cancel answers 400 "Only Draft or Submitted documents can be cancelled (current: Approved)". The document stays until stock arrives.
- Suggested fix: allow Cancel (and Reject back to Draft) on Approved for Supervisor and Admin (Q3), show a shortage line in the Approve dialog when `lines[].available` is below `baseQty` and negative stock is off, and use different copy when the reader may post. Owner: ui-ux-designer for Q3, then be-framework and fe-framework.

**PR-03 Minor, Reports: From later than To shows "Something went wrong"**
- Steps: Reports, set Type = Issue, From 12 Oct 2026, To 10 Oct 2026.
- Expected: the API's message "The 'from' date is after the 'to' date." (400, under `errors.to`) next to the To field, the table cleared.
- Actual: the red generic "Something went wrong." with Retry, and the old footer "1-6 of 6" stays. Retry cannot succeed.
- Suggested fix: map `errors.to` to the field, or disable the range, and reset the page footer on error. Owner: fe-framework.

**PR-04 Minor, Stock "Low stock only" disagrees with Low stock, Home and the badge**
- Steps: with never-stocked products that have a safety level (59 here, plus QA-003), open Stock and tick "Low stock only"; compare with Low stock.
- Expected: the same set, or a label that says why not.
- Actual: Stock shows 3 rows (DEMO-001, QA-001, QA-004) while Low stock, Home and the badge say 62 (later 64). Documented backend decision (the stock list only has rows that exist), but the screen label promises the same thing.
- Suggested fix: remove the toggle in favour of the Low stock screen, rename it "Low stock only (items with a stock row)", or let the stock list include never-stocked products. Owner: ui-ux-designer, then be-framework.

**PR-05 Minor, Receipt and issue lists: the search box promises remark search but finds nothing**
- Steps: Receipts, type "bulk rcv 12" (a remark) or "big delivery"; the placeholder says "Number, partner or remark".
- Expected: the documents with that remark.
- Actual: "No documents match these filters." (also in the API with `Search=`). Number and partner work (a partial number such as 0100 finds RCV-WH01-2610-0100).
- Suggested fix: add Remark to the documents search or change the placeholder. Owner: be-framework (or fe-framework for the text).

**PR-06 Minor, Reports table: SKU and date wrap, and product name, unit and unit cost are only in Excel**
- Steps: Reports at 1280x720, receipt and issue detail.
- Expected: SKU on one line; the design (3.9) lists posted, document, type, status, warehouse, partner, SKU, product, qty, unit, base qty, stock effect, unit cost, amount.
- Actual: SKU wraps at the hyphen ("QA-" / "002", "BULK-" / "0300"), the posted date wraps over three lines so every row is tall; the on-screen table has no Product, Unit or Unit cost column and the Amount column is partly outside the visible region (the region scrolls sideways).
- Suggested fix: no-wrap on SKU and date, give the table the width of the content area (or move Partner to a second line), add Product and Unit cost for roles with `cost.view`. Owner: fe-framework.

**PR-07 Minor, Stock received by Warehouse staff has zero cost, so its value and issue estimates are 0.00** (needs product decision)
- Steps: staff post a receipt of 200 DEMO-002 and 10 QA-004 (no cost field for staff). Look at Stock (supervisor) and at an issue of QA-004 in the inbox.
- Expected: some visible cost or a way to correct it.
- Actual: DEMO-002 shows 200 PCS and Value 0.00, the override issue of QA-004 shows "0.00 est."; the Home Stock value shows 0.00 until a receipt with a cost arrives. Documented as "not done" in the cost-visibility handoff (no cost correction feature). Approval-by-amount cannot fire for such stock.
- Suggested fix: decide on a cost-correction or an opening-cost step, and show a "no cost" hint to supervisors on rows with zero cost. Owner: product-manager, ui-ux-designer.

**PR-08 Minor, Movements Excel: a reversal row cannot be told from its posting row**
- Steps: read `receipt-issue-detail.xlsx` for ISS-WH01-2610-0002 (reversed issue).
- Expected: a marker like the screen's Reversal badge.
- Actual: two rows with the same document, type, status ("Reversed"), qty and no flag; only the sign of Stock effect (-40 / +40) and the date differ. The columns are Posted (MYT), Document, Type, Status, Warehouse, Partner, SKU, Product, Qty, Unit, Base qty, Stock effect, Unit cost, Amount (Stock effect and cost columns are present for the supervisor; the sums per product equal the stock on hand: QA-002 735, QA-004 -15, DEMO-001 2, QA-001 2,000, DEMO-002 200).
- Suggested fix: add a "Reversal" (Yes/blank) column or put "Reversal" in the Status of that row. Owner: be-framework.

### Polish

- **PR-09** The stale banner raised by the tab check (before any click) says "Someone else changed this document", without name and time; only the 409 path names the user and the time (E4). Use the list item's `submittedByName` or fetch the audit, or accept and word it as such.
- **PR-10** Low stock is ordered by absolute shortfall (50, 18, 15, 5), the design says worst first by shortfall percentage; Stock opens sorted by SKU, the design says by product name.
- **PR-11** A reversed document shows the reason only ("Reversed" and the text), not "Reversed by {name} on {date}" (UX 4.4 note 4); the date and person are only in History.
- **PR-12** Audit log: all "Show" buttons have the same accessible name "Show" (no row context); suffix search of a document number finds nothing (prefix only); no entity-type dropdown although `/api/audit/filters` returns one.
- **PR-13** The product stock page (`/stock/{id}`) shows "-15 PCS" with no Negative chip, while the Stock list and Low stock do.
- **PR-14** In the Add item dialog, pressing Enter on an exact barcode briefly shows "No product matches" before the lookup answer arrives and the line is added (seen once; a scanner user may read it as a failure). Show nothing or "Looking up..." while the lookup runs.

## Cost and value visibility

Supervisor (UI and API): sees cost and value everywhere (Amount on lists and documents, per-line cost, Stock Value column and sort, Home Stock value tile, ledger cost, approval thresholds, "5,000.00 est."), and the four Excel exports contain the cost columns (stock summary: Avg cost, Value; ledger: Unit cost; movements: Unit cost, Amount). Owner and Admin the same.

Staff, every screen and endpoint I could reach (Home, receipt and issue lists, documents of every status, the editor and the Add item dialog, History and Stock ledger tabs, Stock list and product page, Low stock, Products, Warehouses, Partners; API sweep of 22 endpoints with a money-key scan): no cost or value. `totalAmount`, `unitCost`, `avgCost`, `value`, `totalValue`, approval amounts are `null` (the approval rule list shows only the quantity rule number); "Why this needs approval" reads "Its value reaches an approval limit. Approver: Supervisor."; no cost field in the receipt editor; no Value column and no Export on Stock; Reports, Audit and the Stock value sort are refused (route "You don't have access to this", API 403, `sortBy=Value` 400). Pass.

Clerk: cost shows on receipts and issue documents and in the lists (Amount column 230.10, per-line 10.00 / 2.50 / 4.00, the receipt editor has "Unit cost"), valuation does not (no Home value tile, no Value column, `avgCost`, `value`, `totalValue` null, ledger and summary exports 403). Pass.

## Numbers checked

- Stock value: sum of the 305 stock row values 54,875.4998 = `/api/stock/summary` 54,875.49977 = Home 54,875.50 (before PR-01); `skuCount` 303 = rows with on hand above 0.
- Movements: total 341 = ledger 341 rows (the reversal row exists in both); sum of Stock effect per product = on hand for all five QA/DEMO products; 40 rows and 40 + header in the export agree with the page total; page 1 + page 2 (25 + 15) have no duplicates and no order violation (checked by script across all pages).
- Home Posted today 335 = Posted list 335; Low stock 62 = list total 62 = export 62 rows (63 with header).
- Receipts list 14 pages of 25 (333 rows), search by number and barcode (stock) fine, remark search not (PR-05).
- Speed (API, warm): stock page 10 to 36 ms, last stock page 26 ms, movements page 15 43 ms, audit 47 ms, documents 26 to 46 ms, exports 40 to 83 ms.

## Console and network

- Console errors: only the service worker registration (NG05604, "unknown error fetching the script", the pane does not run service workers; NV-26 is not an app defect), the expected 409 (stale and reversal blocked), the expected 400 (date range), one 401 on the login page before sign-in and the 401 of my deliberate wrong password.
- Network: no 5xx. Every document command carried the token (428 would have been returned otherwise). A full page load of a document fires about 7 reference-data requests plus a document search for the stale check; no problem seen.

## Open product decisions that decide the next round

1. Q3: allow Cancel on an Approved document (PR-02, PS-11).
2. Cost for stock received by Warehouse staff (PR-07).
3. Date format of the date inputs (PS-15, NV-27).
4. Paging controls (PS-13).
5. Whether "Low stock only" on Stock should match Low stock (PR-04).
