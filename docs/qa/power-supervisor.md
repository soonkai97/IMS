# QA report: power supervisor (desktop)

Persona: busy warehouse supervisor at a desk. Date of run: 2026-10-10. Build: web http://localhost:4200 (dev server), API http://localhost:5109, throw-away QA database.
Roles used: supervisor (main), staff and owner (second person, mostly through the API), admin (settings, users, audit).
Method: business flows through the UI where the tooling allowed, and through the API for volume, negative cases and cross-checks (the UI always compared with network calls). Credentials are not recorded here.

Result: 0 Blocker, 9 Major, 10 Minor, 7 Polish. No flow is completely broken; the approval, override, reject, self-approval, reversal, settings and export flows work end to end. The Majors are about information the supervisor needs to decide (value, reason, balance), a wrong error message on a blocked reversal, silent data creation in import, and gaps in low-stock and audit search.

## Environment notes (not product defects, but they limited the run)

- The browser pane had one shared tab (`seed`) used by all three QA agents; no tool opens a second tab. The refresh cookie is per host, so every sign-in in the pane replaced the other agents' session, and the viewport emulation was changed under me several times (screenshots flipped between 1x and 2x). I signed in and out as supervisor and admin in that tab. Expect some noise in the other QA reports.
- No tool selects a local file, so the import wizard (upload step) could not be driven in the browser. Import was exercised through `POST /api/import/products` with a generated .xlsx (preview and commit). The wizard UI itself is untested.
- Login answered 429 a few times while three agents and my scripts were signing in (one IP). Bulk calls above about 550 per minute also hit the 600 per minute limit. Seen only because of the shared test setup, but a supervisor team behind one office address would see the same on a busy morning.
- Settings left at the end of the run: negative stock OFF, self-approval OFF, cool-down 48 h, the extra Receipt rules (qty >= 1,000 Supervisor active; amount >= 100,000 Owner inactive). About 1,100 products (BULK-xxxx), 55 posted receipts, 1,100 drafts and my test users are in the database.

## Findings, sorted by severity

### Major

**PS-01 Major, Document page, reversal blocked by stock shows the wrong message**
- Steps: negative stock OFF. As staff post an issue that takes QA-002 to 0 (ISS drain). As supervisor open the posted receipt RCV-WH01-2610-0001 (contains DEMO-001 5, QA-001 2000, QA-002 500) and press Reverse with a reason.
- Expected (UX E6, section 3.5): "Can't reverse {docNo}. It would make {sku} negative ({balance}). Reverse or adjust the later issues first, or ask an Admin." naming every product that would go negative, inside the reverse dialog.
- Actual: the dialog closes and a red banner says "Stock changed. Stock for DEMO-001 is now -3 PCS, but the document needs 5. Adjust the quantity and try again." That is the E5 post copy: a posted document cannot be adjusted, only the first product is named (QA-002 also fails), and there is no hint to reverse later issues. The raw API detail is "Insufficient stock for product 1 in warehouse 1: available -3.0000, requested 5.0000." (ids and four decimals instead of SKU, warehouse name and units).
- Suggested fix: separate error code or `type` for the reversal case; list all offending SKUs with balances; show it in the reverse dialog and keep the typed reason.

**PS-02 Major, Product import, unknown category and unit are created silently; wrong error text for a bad number**
- Steps: upload a workbook (API: `POST /api/import/products?commit=true`) with rows whose Category is "Nonexistent" and whose Unit is "XYZ" or "Carton" (the unit exists as code CTN, name Carton).
- Expected (UX 3.6 and the wizard text "the category must exist"): row Error `Category "Nonexistent" doesn't exist` and an unknown unit error.
- Actual: preview shows New / ok, commit creates category "Nonexistent", unit "XYZ" and a second unit "CARTON" next to CTN. Catalogue pollution from one typo, and the category and unit lists are admin-only to clean up. Also a non-numeric safety stock ("abc") is reported as "Safety stock cannot be negative." (should say it is not a number). The duplicate-SKU message does not say which other row ("also appears in row N").
- Suggested fix: reject unknown category and unit (match by code or name, case-insensitive), fix the number message, add the other row number.

**PS-03 Major, Approval inbox and document page, the value that triggers approval is invisible; issues show 0.00**
- Steps: with the seeded rule (Issue amount from 5,000) a 4,500 x QA-001 issue (valued about 2.5 per unit) needs approval. Look at it in Issues and in Approvals.
- Expected: the supervisor sees quantity and value, and why approval is needed ("over the 5,000 limit").
- Actual: every unposted issue shows Amount 0.00 (ISS-WH01-2610-0011: 4,500 units, Approved, 0.00; ISS-0017 shows 423.86 only after posting). The document page shows no totals at all, only per-line quantity and unit cost; the confirm dialog says only "The document can then be posted." The inbox has no Value column. The "Sent for approval" toast gives no reason. Receipts show amounts (11,800.00) so the screens disagree.
- Suggested fix: compute and return the estimated value for issues at submit (the server already values them for the rule), show totals and the matching rule on the approver view and in the inbox.

**PS-04 Major, Low stock and Stock, products never stocked and negative balances are not flagged**
- Steps: create a product with safety stock 5 and no receipt (five such products exist: QA-IMP-001 safety 5, -004, -006, -008, -009). Also post an issue below zero for a product with safety stock 0 (QA-004 went to -40).
- Expected: UX 3.8 "on-hand <= safety stock": a never-stocked product with safety 5 is the worst low-stock item (the wireframe even lists "Washer 0 PCS LOW"). A negative balance should stand out.
- Actual: the Low stock list and the Home "Low stock" tile only count rows that exist in the stock table, so those products never appear. QA-004 at -40 BOX has no Low chip (safety 0) and no other warning; the on-hand number is just shown in black.
- Suggested fix: include active products with safety stock above zero and no row (on-hand 0); treat on-hand below zero as Low (or add a "Negative" chip).

**PS-05 Major, Add item dialog, the search box is not focused (keyboard and scanner path broken)**
- Steps: New issue, press Add item (keyboard or mouse), type a barcode, press Enter.
- Expected (UX D8 and 4.4): search focused, Enter on an exact barcode adds the line.
- Actual: focus sits on the dialog container, the typed text goes nowhere, and a first Tab lands on the X so Enter closes the dialog with nothing added. After clicking into the box, typing `BC-QA-002` and Enter adds the line correctly ("QA-002 added.", qty 1), so only the autofocus is missing. A Bluetooth wedge scanner (the stated MVP scan path) would scan into nothing.
- Suggested fix: focus the search input when the dialog opens; keep focus in it after each add.

**PS-06 Major, Audit log, the Object filter is exact-match on the technical entity type**
- Steps: Audit log, type "Document" (or a document number such as ISS-WH01-2610-0017) in Object.
- Expected: find the history of one document.
- Actual: "No activity for these filters." Only the exact value `StockDocument` works (the API filter is `EntityType`, case-insensitive exact). Document numbers cannot be searched at all; the empty state has no Clear filters action. The User filter is also exact (`staf` finds nothing). Nobody can know the entity names (`StockDocument`, `SystemSetting`, `ProductBarcode`).
- Suggested fix: a dropdown of entity types, plus a search box for the entity id or document number; partial match on user.

**PS-07 Major, Documents API, `allowedActions` offers Approve and Reject that always fail for a Supervisor on an Owner-level document**
- Steps: add a Receipt rule "amount from 100,000, approver Owner" (rule exists, inactive now). Staff submit a receipt of 900 x 200 (180,000, `isLarge` true). Read the document as supervisor.
- Expected (UX D3): the UI renders only what the user may do.
- Actual: supervisor `allowedActions` = `cancel, approve, reject`, but Approve answers 403 "This document is above your approval limit". The inbox and the Home badge handle it (client mirror, badge 2 vs 3 raw rows), but the document page uses `allowedActions`, so a supervisor sees an Approve button that never works. (API-verified; not repeated in the UI because the rule was inactive by then.) Related: the supervisor's raw `requiresApproval=true` list contains the large document, the Owner's raw list contains the non-large ones, so the client mirror has to be exact (fe-framework already noted the missing `canAct` on list items).
- Suggested fix: compute `approve` and `reject` against the approver limit in `allowedActions`; add `canAct` to list items.

**PS-08 Major, Document page, stale-document banner is wrong twice**
- Steps A: open an approval document (RCV-WH01-2610-1177), cancel it as staff in the API, press Approve in the UI, press Reload in the banner. Steps B: reverse a posted issue (ISS-WH01-2610-0017) in the UI.
- Expected: A: the banner disappears after Reload. B: no banner after your own successful action.
- Actual: A: the banner works (409 explained, no toast), but after Reload the red "changed since you opened it" banner stays above the freshly loaded Cancelled document, and the Approve and Reject buttons stay enabled while stale. B: the Reverse succeeded (toast "Reversed", status Reversed, ledger rows written), yet the red banner "ISS-... changed since you opened it. Your changes were not saved." appeared at the same time. The network log shows a documents search request just before the reverse POST, so it looks like a late stale-check response from the tab-focus check being compared with the new token. The message also lacks who and when (UX E4).
- Suggested fix: clear the banner on reload, ignore a stale-check result that started before the last successful command, name the other user and time.

**PS-09 Major, Documents API, reasons shorter than 5 characters are accepted**
- Steps: API calls with `negativeStockOverrideReason: "abc"` on approve, `reason: "no"` on reject, `reason: "x"` on reverse.
- Expected: UX 6.4 "Enter a reason of at least 5 characters", and the audit entry is the control for an override.
- Actual: all three answer 200 and store the short text (override reason "abc" is in the audit trail). The UI blocks it ("Enter at least 5 characters", confirmed for approve), so only another client or a script gets through. Empty reason is rejected correctly (400 with a field error).
- Suggested fix: enforce min length (trimmed) in the validators of the three commands.

### Minor

**PS-10 Minor, Documents API, a submitter can reject their own submitted document**
- Supervisor submitted a document needing approval; Approve answers 400 "You cannot approve a document you submitted" (good, and the UI shows the E3 sentence with a disabled Approve), but Reject by the same user answered 200 and returned the document to Draft with a Returned flag. `allowedActions` hides Reject for the submitter, so the API and the contract disagree (UX 7.2 blocks Reject for the submitter). Harmless in effect (it is a recall) but it makes the Returned list and counts dirty.

**PS-11 Minor, Approve on an issue that is already short (negative stock OFF)**
- With negative stock off, ISS-WH01-2610-0011 (4,500 of QA-001, only 2,000 on hand) was approved with no warning and failed only at Post (409, then the E5-style banner). Better to warn at approval. The seeded "approved but cannot post" document then sits with only Post, which fails, and no Cancel (UX Q3).

**PS-12 Minor, Audit log content**
- One settings save writes three rows (`updated` for each of two SystemSetting keys plus `settings.updated`). Login success rows (about 20 in a test hour) bury business events; no entity-type or action filter to hide them. The Details popup shows raw JSON with numeric enums (`"Status": 3` to `"Status": 4`), and settings and rule changes name keys but not old and new values in plain words.

**PS-13 Minor, Lists, paging has only Previous and Next**
- 1,178 receipts = 48 pages; the oldest page needs 47 clicks, no first or last, no page size. After pressing Next at the bottom of the table the scroll position stays at the bottom. API response times were fine (25 rows in 30 to 80 ms at 1,100+ rows for stock, ledger, movements, documents, audit; first stock page 416 ms cold). Search and filters work. Documents list has no export.

**PS-14 Minor, Stock ledger on the product page**
- Columns are document, date, type, warehouse, change, user. The design asks for balance after; without it a supervisor cannot read the history (I reconciled ledger sums to stock by script: 1,122 ledger rows, 0 mismatches). The override reason is not linked from the ledger row of a below-zero issue. The ledger tab on the document has no date or user.

**PS-15 Minor, Reports**
- Date inputs use the browser format (mm/dd/yyyy) while the app writes "10 Oct 2026". From later than To gives an empty table with no warning; the empty state "No data for these filters." has no action and Export disappears. Quantity is unsigned, so a Reversed document shows its original quantity as a positive line with no reversal line (net effect not visible; the status column says Reversed). The movements report and the ledger are consistent otherwise: ledger 1,115 = movements 1,112 + 3 reversal rows at the time. The date filter day boundary (KL midnight) was not tested.
- Export: from the UI `GET /api/reports/movements/export?Type=Issue` (current filter) 200 and the file saved by the browser; no toast or message after pressing Export Excel.

**PS-16 Minor, Home counts**
- "Posted today" counts reversed documents too: Home 66, list Status=Posted 63 (3 reversed); later 68 vs 64 posted + 4 reversed. "Products with stock" shows 1,106 but only 1,101 have on-hand above 0 (four at 0, one negative).

**PS-17 Minor, Approver wording**
- Reject dialog says "It goes back to staff as a draft" (no name; the toast says "Returned to staff"); override dialog does not list which lines go to what balance (UX 4.5 wanted "QA-004 will go to -40"); document page shows "Created by" but no "Submitted by/at"; Approvals inbox has no Review button (whole row is the link, no hover cue).

**PS-18 Minor, Approval rule boundary**
- Rules fire at "from" (>=). A receipt of exactly 1,000 units needed approval under "Quantity from 1,000". The settings table says "from", which is fine, but UX and the design text still say "over {threshold}".

**PS-19 Minor, Product import template**
- The downloaded template's sample row uses barcodes that already belong to the demo products (DEMO-001, DEMO-002), so uploading the unchanged template shows an Error row ("Barcode ... already belongs to DEMO-001"). Use clearly fake barcodes or none.

### Polish

- **PS-20** The header shows "New receipt" while an existing document loads.
- **PS-21** The stock search box shows two clear (x) icons (native and custom).
- **PS-22** SKU wraps at the hyphen ("QA-" / "002") and document numbers wrap in narrow table columns (Reports, Receipts at about 1000 px).
- **PS-23** "Set to 0" is offered for a line with 0 available; it creates a zero-quantity line.
- **PS-24** Edit user dialog: three checkboxes have the accessible name "on"; the cool-down error says "This value is too large." without the maximum (720).
- **PS-25** API: an `If-Match` header without quotes is silently ignored (no concurrency check), a quoted wrong value answers 409 correctly; a bare token from an API client gets no protection.
- **PS-26** Keyboard: sign-in by Tab and Enter works; dialogs close on Esc and return focus to the button; the confirm dialog opens with focus on the container, not on a button (fine for destructive, but one more Tab than needed for Post). After Post the Reverse button appears exactly where Post was (a double click can open the Reverse dialog; the dialog still needs a reason).

## Flows completed

| # | Flow | Result |
|---|---|---|
| 1 | Staff submits a large receipt (rule qty >= 1,000): lands in Approvals, supervisor approves, posts, stock and ledger updated | Pass (UI and API) |
| 2 | Staff issue below zero with negative stock ON: Submitted + Override flag; approval needs a reason (empty 400, UI min 5), Post goes negative, history shows reason | Pass in UI; API accepts short reasons (PS-09) |
| 3 | Same issue with negative stock OFF: blocked at submit (409), inline E1 in the form, Submit disabled | Pass |
| 4 | Approvals inbox: oldest first, Override flag, tabs, count equals Home badge | Pass (Large and Owner view API only) |
| 5 | Reject with reason, creator sees Returned (counts, flag, banner), resubmit clears it | Pass (API for staff side; supervisor sees banner in UI) |
| 6 | Self-approval blocked (API 400, UI disabled Approve with E3 sentence); setting "allow own approval" ON lets it through | Pass |
| 7 | Reversal with reason: stock and ledger opposite rows, document Reversed, second reverse refused | Pass; blocked case fails UX (PS-01); false stale banner (PS-08) |
| 8 | Stock list, search, low only, category, ledger per product, paging 1,106 rows | Pass with PS-04, PS-14 |
| 9 | Movements report filters (type, date, warehouse), paging, order, Export Excel from the UI and all four exports through the API (stock summary 1,105 rows, ledger 1,115, movements 1,112, low stock 349, all complete, not cut to the page) | Pass with PS-15 |
| 10 | Low-stock list and Home tile | Partial (PS-04) |
| 11 | Audit log: who, what, when, details, paging | Pass for content; filters weak (PS-06, PS-12) |
| 12 | Stale document, two actors | Detected and explained; banner defects (PS-08) |
| 13 | Product import preview and commit with errors: preview counts, all-or-nothing commit refused with errors, unit change with stock guarded | Pass through the API only; UI upload not testable; content defects (PS-02) |
| 14 | Settings: negative stock, own approval, cool-down (0 to 720), approval rule validation; change takes effect at once (E1 appeared right after un-ticking) | Pass |
| 15 | Users as admin: create (12 character password rule), duplicate 409, role change, deactivate (login 401), cannot demote or deactivate self (UI shows the message) | Pass; "last admin" is only a self-guard (two admins demoting each other at the same moment was not tried) |
| 16 | Large lists: 1,100 products, 1,100 drafts, 55 posted receipts; stock, ledger, movements, documents, audit pages | Pass for speed (API 4 to 80 ms after warm-up); paging UX Minor (PS-13) |
| 17 | Keyboard use: sign-in, dialog Esc, Add item scanner path | Partial: PS-05 |

## Number checks (values seen)

- Stock summary (API) 301,444.9972 = sum of the 1,105 stock row values 301,444.9972; Home shows 301,445.00 (rounded, consistent). Later 300,844.9972 equals the rows again.
- Ledger sums equal stock on-hand for every product (1,122 rows, 0 mismatches). Ledger 1,115 rows = movements 1,112 + 3 reversal rows; stock rows 1,105 = 1,100 BULK + 5 others.
- Home Low stock 349 = Low stock list total 349 = stock `LowOnly` total 349 (later 350 = 350 = 350).
- Home Waiting for approval 2 = inbox "Waiting for me" 2 (later 5 with my new documents, 3 after handling) = sidebar badge.
- Inconsistent: Posted today 66 (list of Posted 63; includes 3 Reversed); Products with stock 1,105/1,106 (rows above 0: 1,102/1,101); issue Amount 0.00 until posted while approval used a valuation (PS-03); Stock value includes negative lines (-250 for DEMO-001 at one point), so the headline value can be lower than the sum of positive stock.
- Doc numbers are consistent (RCV-WH01-2610-nnnn, ISS-WH01-2610-nnnn, nnnn counts per warehouse and month; ids differ from numbers, expected).
