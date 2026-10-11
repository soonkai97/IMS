# IMS Phase 1 (MVP) - UX design

Author: ui-ux-designer. Source: `docs/private/prd-v0.2.txt` (3, 5, 6, 7 Fig 2 and 5), `TASKS.md` T08-T19.
Scope: Phase 0 login + Phase 1 screens only. Scanning, stocktake, transfers, POs, offline, print, UoM conversion are Phase 2 and are not designed here (hooks are noted where they are free).
Status: ready for fe-designer (tokens, component look) and fe-architect (routes, component contracts).

## 0. Decisions and open questions

Decisions (others must respect; reasons are task speed or error prevention):

| # | Decision | Why |
|---|---|---|
| D1 | Phone-first for Warehouse staff and Clerk (360px, bottom tab bar, sticky bottom action bar, 48px targets). Desktop-first for Supervisor, Owner, Admin (sidebar + tables). One codebase, one set of components, breakpoint at 1024px. | Staff do receive/issue one-handed. Supervisors review and report at a desk. |
| D2 | "Rejected" is not a status. Reject returns the document to Draft and keeps the reason (PRD Fig 5). The Draft shows a "Returned" badge and a reason banner. | Matches PRD; TASKS T10 lists Rejected separately, so be-system-designer must model it as a flag + reason on Draft. |
| D3 | The API returns `allowedActions[]` on every document for the current user (computed server-side from role, status, creator, warehouse, approval/override flags). The frontend renders buttons from it and never re-implements the permission rules. Tables in section 7 are the spec for that server logic. | Rules (no self-approval, large-doc Owner approval, no-approval posting) are configurable and server-owned. Prevents UI/API drift. |
| D4 | Staff with Post rights get one button, "Submit & post". The server decides whether approval is needed. If yes, the document stops at Submitted and a toast says why. If no, it posts after one confirm. | Saves 2-3 taps on the commonest path (no-approval receipts and small issues). |
| D5 | Insufficient stock is blocked inline on the line, at edit time and again at submit and post. With negative stock allowed, the document goes to a supervisor with `requiresOverride`; the override reason is entered at approval, not by the submitter. | Fig 2: user first adjusts quantity; override only if the system allows. Keeps the override a supervisor act and keeps self-approval rule intact. |
| D6 | Product import is all-or-nothing: Confirm stays disabled until the preview has 0 error rows. The error list is downloadable. | Simplest for users to reason about; avoids half-loaded catalogues. |
| D7 | Opening-stock import creates Draft receipts (type "Opening stock", one per warehouse) for a supervisor to review and post. Nothing hits stock until posted. | MD-05 says "creates opening documents"; Draft keeps BR-01/BR-02 review step. BA to confirm. |
| D8 | Text search boxes accept keyboard-wedge input, so a Bluetooth scanner works in MVP: Enter on an exact barcode/SKU match adds that product directly. | Free speed-up before Phase 2 camera scanning (SCAN-01). |
| D9 | Copy uses ICU-style placeholders (`{sku}`, `{available}`) and is kept in i18n files (en first; zh, ms later). No sentence is built by concatenation. | PRD 9: UI in zh/en/ms. |
| D10 | Dates shown `dd MMM yyyy HH:mm` in Asia/Kuala_Lumpur (BR-10). Quantities show up to 3 decimals, trailing zeros trimmed. | Consistency with BR-10. |

Open questions (default in brackets is what this design assumes):

| # | To | Question |
|---|---|---|
| Q1 | business-analyst | The permission matrix in the PRD text is garbled in the extract. Section 1.1 states how I read it; please confirm, especially Master data for Clerk ("Yes (drafts)"), Supervisor on Reports, and Stock view for Clerk. [Clerk and partners answered by the product owner on 2026-10-10: Clerk may create partners, not edit or deactivate them.] |
| Q2 | business-analyst | After a supervisor approves, may the submitting Warehouse staff post? [No, per matrix: Post is Admin/Supervisor only once approval was required.] I recommend Yes, because staff are the ones holding the goods. |
| Q3 | business-analyst | Approved documents cannot be cancelled in Fig 5. If posting then fails (stock moved), the document is stuck. [Design follows PRD; Approved shows Post only.] Recommend allowing Cancel on Approved for Supervisor/Admin. |
| Q4 | be-system-designer | Does reversal create a separate document number? [Design shows original as Reversed with a "Reversed by ... on ... reason ..." block and two ledger lines; no second document.] |
| Q5 | be-system-designer | When is the document number assigned: at first save or at posting? [At first save; list shows "New" until then.] |
| Q6 | business-analyst | Receipt line unit cost in MVP? [Optional column on desktop, inside "More" on phone; STK-04 valuation is not MVP. Decided 2026-10-10 (1.4): only roles with `cost.view` see and enter it; the Warehouse staff receive by quantity and the server applies the last known cost.] |
| Q7 | business-analyst | Locations (MD-02) are optional. [Hidden everywhere unless Admin enables locations; then a required Location field appears on lines.] |

## 1. Roles, permissions and menus

### 1.1 How the permission matrix is read (PRD 3.1)

Warehouse scope: Warehouse staff, Supervisor and Clerk only see warehouses assigned to them; Admin and Owner see all.

| Feature | Admin | Owner | Supervisor | Warehouse staff | Clerk |
|---|---|---|---|---|---|
| User and role management | Yes | -- | -- | -- | -- |
| Master data (products, warehouses, partners) | Yes | Read | Yes | Read | Partners: create only (no edit, no deactivate); products and warehouses Read (decided by the product owner, closes Q1) |
| Create receipt / issue | Yes | -- (read documents) | Yes | Yes | Yes, drafts and submit |
| Approve | Yes | Yes, large documents only | Yes | -- | -- |
| Post | Yes | -- | Yes | Yes, only when no approval is required | -- |
| Reverse a posted document | Yes | -- | Yes, reason required | -- | -- |
| View stock and ledger | Yes | Yes | Yes | Yes, own warehouses | Read, own warehouses |
| Reports and export | Yes | Yes | Yes (Q1) | -- | -- |
| Audit log | Yes | Yes | -- | -- | -- |
| See cost (unit cost, line cost, document totals, approval amounts) | Yes | Yes | Yes | -- | Yes |
| See stock value (average cost, value, stock total, ledger cost) | Yes | Yes | Yes | -- | -- |
| Stocktake, transfers | Phase 2, not designed | | | | |

Cross-cutting rule: a user cannot approve a document they submitted (BR-07, configurable). Shown as a disabled Approve with the reason, never as a missing button, so the user understands why (see error E3).

### 1.2 Menu per role

Legend: **Yes** = full; **Read** = view only (no create/edit buttons rendered); `--` = item not in menu (route also guarded; direct URL shows S03 Forbidden).

| Menu item | Admin | Owner | Supervisor | Warehouse staff | Clerk |
|---|---|---|---|---|---|
| Home | Yes | Yes | Yes | Yes | Yes |
| Receipts | Yes | Read | Yes | Yes | Yes (own drafts + submit) |
| Issues | Yes | Read | Yes | Yes | Yes (own drafts + submit) |
| Approvals (badge = waiting count) | Yes | Yes (large only) | Yes | -- | -- |
| Stock | Yes | Yes | Yes | Yes (own wh) | Read (own wh) |
| Low stock (badge = count) | Yes | Yes | Yes | Yes (own wh) | Read (own wh) |
| Products (incl. Import) | Yes | Read | Yes | Read | Read |
| Warehouses | Yes | Read | Yes | Read | Read |
| Partners | Yes | Read | Yes | Read | Yes (list, New; no edit or deactivate) |
| Reports | Yes | Yes | Yes | -- | -- |
| Users and roles | Yes | -- | -- | -- | -- |
| Settings (negative stock, approval rules) | Yes | -- | -- | -- | -- |
| Audit log | Yes | Yes | -- | -- | -- |

Navigation chrome:

- Phone (< 600px): bottom tab bar, max 4 tabs + "More". Warehouse staff and Clerk: `Home | Receive | Issue | Stock | More`. Supervisor: `Home | Approvals | Stock | Docs | More` (Docs opens a Receipts/Issues switcher). Owner: `Home | Approvals | Stock | Reports | More`. Everything else lives under More (a plain list). Top bar: page title, back arrow on child screens, user menu (name, warehouse, language, sign out).
- Tablet/desktop (>= 1024px): left sidebar with the menu above grouped as Documents / Stock / Master data / Admin; collapsible to icons. 600-1023px: icon rail.
- Home (S02) is role-shaped, not a dashboard: Warehouse staff and Clerk get two large buttons ("Receive stock", "Issue stock") plus "Returned to me (n)" and "My drafts (n)". Supervisor and Admin get "Waiting for approval (n)", "Low stock (n)", "Posted today (n)" and the same two buttons. Owner gets "Waiting for my approval (n)", "Low stock (n)", Reports link.

### 1.3 Screen inventory

Priority: M = must for MVP, S = should (cut first if time is short). Device: P = designed for phone first, D = desktop first (still responsive).

| ID | Screen | Route | Roles (Y = act, R = read) | Pri | Dev | Notes |
|---|---|---|---|---|---|---|
| S01 | Login | `/login` | public | M | P | Lockout message; return URL after expiry |
| S02 | Home | `/` | all | M | P | Role-shaped shortcuts and counts |
| S03 | Forbidden / Not found / Error | `/forbidden`, `**` | all | M | P | One component, three texts |
| S10 | Receipt list | `/receipts` | Adm Y, Own R, Sup Y, WH Y, Clk Y | M | P | `app-list` |
| S11 | Receipt edit / view | `/receipts/new`, `/receipts/:id` | same | M | P | Same component as S13 with `type` param |
| S12 | Issue list | `/issues` | same | M | P | |
| S13 | Issue edit / view | `/issues/new`, `/issues/:id` | same | M | P | Shortfall UI, override dialog |
| S14 | Approvals inbox | `/approvals` | Adm, Own, Sup | M | D | Tabs: Waiting for me / Submitted by me |
| S20 | Stock enquiry | `/stock` | Adm, Own, Sup, WH, Clk(R) | M | P | Filters + export |
| S21 | Product stock + ledger | `/stock/:productId` | same | M | D | Append-only ledger table; links to source doc |
| S22 | Low-stock list | `/low-stock` | same | M | P | "Create receipt" shortcut |
| S30 | Product list | `/products` | Adm Y, Sup Y, others R | M | D | |
| S31 | Product edit / view | `/products/new`, `/products/:id` | same | M | D | Multiple barcodes (add/remove chips), active toggle |
| S32 | Import wizard | `/products/import` | Adm, Sup | M | D | Tabs: Products / Opening stock; 4 steps |
| S33 | Warehouse list + edit | `/warehouses` | Adm Y, Sup Y, others R | M | D | Edit in side panel/dialog; no delete, deactivate only |
| S34 | Partner list + edit | `/partners` | Adm Y, Sup Y, Clk create only (permission `partners.create`: [New] shown, row edit and deactivate hidden), others R | M | D | Supplier/Customer type filter; deactivate only; a duplicate name and type answers 409 |
| S40 | Reports | `/reports` | Adm, Own, Sup | M | D | Tabs: Stock summary / Receipt-issue detail / Stock ledger; filter form, table, Export Excel |
| S50 | Users and roles | `/users` | Adm | M | D | Role + assigned warehouses |
| S51 | Audit log | `/audit-log` | Adm, Own | S | D | Read-only list, filters (actor, entity, date); expand row for before/after |
| S52 | Settings | `/settings` | Adm | S | D | Negative stock on/off; approval rules table; self-approval on/off. May ship as seeded defaults first. |

### 1.4 Cost and value visibility (product-owner decision, closes EA-23)

Warehouse staff never see any cost or value. The Clerk sees cost on the documents they work on, not what the stock is worth. Supervisor, Owner and Admin see everything. It is decided by two permissions, never by role name, and both are in the `permissions` list the web app receives at sign-in (`cost.view`, `valuation.view`); the app shows a cost or value only when it holds the permission and treats a `null` as "not for you" (render nothing, no placeholder like 0.00).

| Permission | Roles | What it unlocks |
|---|---|---|
| `cost.view` | Clerk, Supervisor, Owner, Admin | Unit cost and line cost of documents; document totals and estimates (`totalAmount`); approval amounts (`approvalTriggers[].threshold/actual` of an amount rule, `approval-rules[].amountThreshold`); cost columns of the receipt/issue detail report and its export; the cost column of the opening-stock template; cost keys in audit entries |
| `valuation.view` | Supervisor, Owner, Admin | Average cost and value on stock rows; the stock total value (the Home value tile); the unit cost of ledger rows; "Avg cost", "Value" and the ledger "Unit cost" columns of the stock summary and ledger exports; sorting stock by value |

The one rule (backend): a cost or value is tagged `[VisibleWith(permission)]` where it is declared. The pipeline step `CostVisibilityBehavior` then sets every tagged field of a response to `null` for a caller without the permission (nested objects, lists and pages included), removes tagged keys from the audit log's before/after JSON, leaves tagged columns out of Excel files (`IReportWriter`), and answers `400` to a tagged sort key (`sortBy=Value`). Architecture tests fail when a response property that looks like cost or value is not tagged, so a new endpoint or export cannot forget it.

- Approval flows keep working without amounts: `requiresApproval`, `isLarge`, `canAct`, `allowedActions` and the approval trigger (`metric`, `roleRequired`) are the same for everyone. A Warehouse staff user sees "Needs approval (amount)" and the role that approves, not the value or the threshold. A quantity rule is not money and shows its numbers to everyone.
- Receipts: the Warehouse staff enter quantities only. If they send a cost anyway it is ignored: each line takes the last known (average) cost of the product in that warehouse, or zero if it was never stocked there, so the receipt does not change the average. Clerk, Supervisor and Admin enter the cost as before. An Opening-stock import is a Supervisor or Admin act, so its cost column stays.
- The audit log is Owner and Admin only (so Staff and Clerk get `403`), and still has its cost keys scrubbed for any caller without the permission.
- Reports and Excel exports are Supervisor, Owner and Admin only; each file leaves out the columns its reader may not see.
- Counts (Home tiles, menu badges) are numbers of documents, not money; the only value tile is the stock total, which needs `valuation.view`.

### 1.5 Costing and reversal (closes PR-01)

Stock is valued at a **moving weighted-average cost** per product and warehouse (BR-05). The value of a row is always `quantity x average cost`; nothing else is stored, so the Home total, the Stock list and the ledger cannot disagree.

- **Receive** at cost `c`: `average = (qty x average + received x c) / (qty + received)`. While stock is zero or negative the new cost simply replaces the average. A receipt at the average itself leaves it exactly as it is.
- **Issue** is booked at the average in force when it posts; the average does not move. An issue that has not posted yet is estimated at the average when it was last saved.
- **Reverse an issue** puts the stock back at the cost the issue left at (a receipt at that cost).
- **Reverse a receipt** takes the quantity out at the cost it came in at **and takes its cost out of the average**. Every posted receipt line remembers the average just before it and the one it produced, and which stock-in came before it (`avgCostBefore`, `avgCostAfter`, `previousLineId` on the document line); the balance remembers the line that brought stock in last (`lastReceiptLineId`), set by EVERY stock-in, also one at the average itself and the return of a reversed issue. An equal average proves nothing about what came in since (a later receipt at the same cost leaves the average where it was), so the marker, not the value, says whether a receipt is still the latest. On reversal:
  1. **The line is still the latest stock-in** (only issues, which never change the average, came after it): the average goes back to the value it had before the receipt, exactly, however many units were issued in between, and the stock-in before it becomes the latest again. Example: 735 PCS at 4.76, a duplicate receipt of 600 at 200.00 (average 92.51), reversed: 735 PCS at 4.76 again, value 3,498.60, not 67,991.90. If 60 PCS were issued in between, the reversal leaves 675 PCS at 4.76. If that average was 0 (a product never costed) and the reversal takes the quantity below zero, the last known cost is kept instead of 0.
  2. **Anything else came in after it, even at the same cost:** the stock loses `received x its cost` of value and the remaining units share what is left, `(qty x average - received x cost) / (qty - received)`. This is exact when nothing was issued since, so the stock value stays equal to the value of the movements (reversing the first of 100 at 10 and 100 at 20 leaves 100 at 20; the first of 100 at 10 and 100 at 10 leaves 100 at 10). After such a reversal no line is the latest any more (the recorded averages of the later receipts still contain this one), so those are taken back by the same arithmetic.
  3. **No sound arithmetic** (nothing would be left, or later issues already used up more value than remains): the average stays as it is now. It is the last known cost, and the next receipt replaces it when stock is zero or negative. The result is never above the largest cost the column holds (999,999,999,999 per base unit).
  - Limit of 2 and 3: with issues in between, the average can end above every remaining receipt cost, because those issues were booked at the blended average (reversing the first of two receipts after issues leaves value `qty x average - received x cost`, not a replay of the history). It is bounded and documented, not capped at the highest remaining cost, because no remaining cost is known without replaying the ledger.
- A reversal that would take stock below zero is still refused when negative stock is off (E6). The quantity rule decides whether a reversal is allowed; the cost rule above decides what the average becomes. The issues booked after a receipt keep the cost they were booked at (the ledger is append-only); only the average of the stock that remains is corrected.
- Receipts posted before this rule existed have no remembered average; they use rule 2, which is exact to the precision of the stored average: those averages were rounded to 6 decimals, and a receipt a million times larger than the stock before it multiplies that rounding by a million (an error of up to 0.5 per unit), so the claim of an exact restore is only made for receipts that carry a snapshot. Averages that the old code left contaminated are repaired once with `docs/dba/15-repair-reversal-average.sql`.
- **Internal numbers and money.** The cost per base unit and the average are INTERNAL numbers: 10 decimal places (`Limits.CostDecimals`, SQL `decimal(22,10)`, 12 integer digits), rounded half away from zero (`Limits.CostRounding`), so repeated blending and unit conversions drift far less than at 6 places. They are not amounts. What a person sees is either money with 2 decimal places (a line amount, a total, an estimate, a stock value) or a unit price with 4 (a derived cost per base unit and the average on screen or in Excel); both follow the money rule of the money-precision change, and until that change is deployed those values leave the API unrounded (up to 14 decimals).
- **Range.** A unit cost may be entered up to 1,000,000,000 per unit (400 above, `Messages.CostTooLarge`), and the cost per base unit it becomes may not exceed 999,999,999,999 (a cost per carton spread over a fraction of a piece can: 400 `CostTooLargeForUnit`). Both are checked before the database sees them, so an out-of-range cost is never a 500.

## 2. Reusable components (the whole set)

Five core components plus three tiny helpers. Everything above is composed from these. fe-designer styles them; fe-architect wraps them as `app-*`.

| Component | Used for | Behaviour spec |
|---|---|---|
| `app-list` | S10 S12 S14 S20 S22 S30 S33 S34 S50 S51, report tables | One data definition, two renderings: table at >= 1024px, card list below. Props: columns (key, label, `primary`/`secondary`/`hideOnPhone`), rows, loading, error, empty slot, page size 25, server paging/sort. Row tap = navigate. Filters are a slot above (search + up to 3 chips/selects). Shows skeleton, empty and error states itself (section 6). |
| `app-form` (+ `app-field`) | S01 S11 S13 S31 S33 S34 S50 S52, dialogs | Label above input, always visible. Error text under field, linked with `aria-describedby`. Required marked "(required)" in the label, not an asterisk only. Numeric fields use `inputmode="decimal"`. Inputs >= 16px font, 48px high. Validation on blur, then on submit; focus jumps to first invalid field. Unsaved-changes guard on leave. Submit button disables and shows a spinner while the request is in flight (prevents double posting). |
| `app-status-chip` | lists, headers | Input: status (+ optional flag). Icon + text + colour, never colour alone (section 7.1). |
| `app-confirm-dialog` | post, reverse, reject, cancel, approve-with-override, discard changes | Title, body, optional reason textarea (`reasonRequired`), cancel label, confirm label (a verb), `destructive` style. Phone: bottom sheet; desktop: centred modal 480px. Focus trapped, Esc closes, confirm button never pre-focused for destructive actions. |
| `app-toast` | every success and non-blocking error | Success: auto-dismiss 4s. Error: stays until dismissed, `role="alert"`. Phone: above the bottom action bar; desktop: top right. Max 2 stacked. Has an optional action ("Undo" is not offered; "View" and "Retry" are). |
| `app-page-header` (helper) | every screen | Title, status chip slot, primary action slot, breadcrumb on desktop. |
| `app-empty-state` (helper) | inside `app-list` | Icon, one line, one CTA (section 6.2). |
| `app-product-picker` (helper) | S11 S13 add-item sheet, S20 search | Typeahead after 2 characters (SKU, name, barcode), 300ms debounce, shows SKU, name, on-hand in the document's warehouse. Enter on an exact barcode/SKU match selects it (D8). Deactivated products are not returned. |

Interaction constants: min touch target 48x48px on phone, 8px spacing grid, body text 16px, contrast AA (4.5:1), visible focus ring, no hover-only actions, every icon-only button has an aria-label. Destructive or irreversible actions (post, reverse, reject, cancel) always go through `app-confirm-dialog`; everything reversible (save draft, filter, add line) does not.

## 3. Task flows

Notation: `[Button]`, `(screen)`, `{system}`. Tap counts assume a known product, one assigned warehouse, and an existing session.

### 3.0 Document lifecycle (PRD Fig 5)

```
                  Save                Submit                 Approve               Post
   (new) -----> DRAFT ------------> SUBMITTED ------------> APPROVED ------------> POSTED --Reverse--> REVERSED
                  ^  \                 |   \                                         
                  |   \Cancel          |    \--(no approval needed)---- Post --------^
                  |    v               | Reject (reason kept)
                  |  CANCELLED <-------+ Cancel
                  +-------------------/
```

Draft and Submitted can be cancelled. Approved cannot (Q3). Posted can only be reversed. Reversed and Cancelled are terminal.

### 3.1 Receive stock (Warehouse staff, phone) - target: 8 taps for one line

1. Home -> [Receive stock]. {New receipt opens; warehouse prefilled if the user has one warehouse; date = today.}
2. (Optional) Pick supplier; type the supplier's delivery order number in "Reference".
3. [+ Add item]. {Bottom sheet opens, search focused.}
4. Type 2+ characters of SKU/name (or scan with a Bluetooth scanner and press Enter). Tap the product.
5. Enter quantity on the numeric keypad (or +/- steppers). [Add and next] to add another, or [Add] to close.
6. Repeat 3-5 per item. Lines list shows product, quantity, remove.
7. [Submit & post]. {Server checks rules.}
8. Confirm dialog "Post RCV-...?" -> [Post]. {Toast "Posted. Stock updated." Document opens read-only with chip Posted.}
   - If approval is required: step 7 submits only; toast "Sent for approval: quantity reaches {threshold}." Chip Submitted, sub-label "Awaiting approval". No confirm dialog.
   - Clerk (no Post right): button is [Submit]; toast "Submitted for approval."
   - Leave early: [Save draft] anytime; unsaved-changes guard on back.
   - Error paths: deactivated or unknown product (E-table, section 6.4); network failure keeps the form and shows a retry toast.

### 3.2 Issue stock (Warehouse staff / Clerk), including insufficient stock and negative-stock override

Happy path mirrors 3.1 with Purpose (Sale / Use / Scrap) required and Customer optional. Differences below.

A. Insufficient stock, negative stock NOT allowed (default, BR-03)
1. While adding a line, the picker shows available stock in the document's warehouse ("12 PCS available"). If qty > available the quantity field shows E1 immediately, with [Set to {available}].
2. A line already added keeps a red inline message and a red left border. The footer shows "{n} lines short" and [Submit] is disabled with the reason in text under it.
3. User taps [Set to {available}] (one tap fixes the line) or edits quantity or removes the line.
4. {Server re-checks on submit and on post.} If stock moved in between, E5 appears as a toast + the affected lines are marked again; the document stays editable (Draft) or stays Submitted/Approved with Post failing.

B. Insufficient stock, negative stock allowed (Admin setting)
1. Lines show an amber (not red) warning: "Short by {short}. A supervisor must approve this issue." Footer shows chip "Override needed". [Submit] is enabled.
2. User taps [Submit]. Dialog: "Send for supervisor override?" listing short lines. -> [Send]. {Status Submitted, `requiresOverride = true`. Appears in Approvals for Supervisor and Admin regardless of value thresholds.}
3. Supervisor opens it from (S14) -> (S13 read view). A shortfall table shows requested vs available vs resulting balance per line.
4. Supervisor taps [Approve]. Dialog "Approve with stock override" with required "Reason for override" textarea (min 5 characters). -> [Approve]. {Reason stored with the document and in the audit log.}
5. Supervisor or Admin taps [Post] -> confirm -> {stock goes negative on those lines; ledger records the override reference}.
6. If the supervisor is also the submitter, Approve is disabled with E3 and another supervisor/Admin must act (unless self-approval is configured off).
7. Reject at step 4: [Reject] with required reason -> document returns to Draft, banner "Returned by {name}: {reason}" (3.3).

### 3.3 Submit, approve, reject

Submit (creator): [Submit] / [Submit & post] -> server returns `requiresApproval`.
- true: toast "Sent for approval. {reason}"; document read-only; creator can still [Cancel document] (recall).
- false and creator may post: confirm and post (3.4). false and creator may not post (Clerk): toast "Submitted. A warehouse user or supervisor can post it."

Approve (Supervisor / Admin; Owner for large):
1. Home tile or menu badge -> (S14 Approvals) -> tap a row. Phone: card; desktop: [Review].
2. (S11/S13 read view) shows lines, totals, requester, warehouse, flags (Override, Large). Primary [Approve], secondary [Reject].
3. [Approve] -> dialog "Approve {docNo}?" (+ reason field if override) -> [Approve]. {Chip Approved; toast "Approved." App returns to the inbox with the next item at top (phone) or stays on the document (desktop).}
4. Supervisor/Admin may then [Post] from the same screen.

Reject:
1. [Reject] -> dialog "Reject {docNo}?" with required "Reason" (min 5 chars) -> [Reject].
2. {Document returns to Draft. Toast "Returned to {name} for changes."}
3. Creator sees it under Home "Returned to me (n)" and in the list with badge "Returned". Opening it shows a banner with the reason, editable form, normal [Submit] again. Banner clears on next submit; the reason stays in the document history.

### 3.4 Post

1. Open a document in Submitted (no approval needed) or Approved status. Primary button [Post] (or [Submit & post] from Draft).
2. Confirm dialog: "Post {docNo}?" Body: "Stock will {increase|decrease} for {n} lines. Posted documents can't be edited, only reversed." Buttons [Back] [Post].
3. {Server posts in one transaction; row-version check.} Success: toast "Posted. Stock updated.", chip Posted, all fields read-only, buttons replaced by [Reverse] (if allowed) and a link "View stock ledger entries".
4. Failure: E4 (document changed by someone else) or E5 (stock changed). Nothing is posted; the dialog closes and the message is shown in a toast and on the document.

### 3.5 Reverse (Supervisor / Admin)

1. Open a Posted document -> overflow menu or [Reverse] (desktop shows it as a secondary danger button).
2. Dialog: "Reverse {docNo}?" Body: "This adds opposite stock entries. The original stays on record." Required "Reason". Buttons [Back] [Reverse].
3. {Server checks the reversal would not push any product below zero (unless negative stock is allowed).} Failure -> E6 listing the products.
4. Success: toast "Reversed. Stock adjusted.", chip Reversed, an info block "Reversed by {name} on {date}. Reason: {reason}", and the ledger tab shows both the original and opposite entries.

### 3.6 Product import with preview and error list (Admin / Supervisor, desktop)

1. (S32) tab "Products" (or "Opening stock"). Step 1 Template: [Download template (.xlsx)] and a short rule list (SKU required and unique, barcode unique, category must exist).
2. Step 2 Upload: drag-drop or [Choose file]. Only .xlsx, max 5 MB (copy in 6.4). {Server parses and validates; nothing is saved.}
3. Step 3 Preview: summary bar `120 rows | 96 new | 20 will update | 4 errors`. Table of all rows with a status column (New / Update / Error); error rows are tinted red and sorted first by default via toggle "Show errors only". Each error row lists problems in plain words (below). [Download error list (.xlsx)] contains row number, column, value, problem. [Confirm import] is disabled while errors > 0, with the text "Fix 4 errors in your file and upload it again."
4. User fixes the file and taps [Upload a different file] (returns to step 2, keeps the same screen).
5. With 0 errors, [Confirm import {n} rows] -> {server imports in one transaction}. Result page: "96 products added, 20 updated." with [View products]. For Opening stock: "3 draft receipts created (Opening stock) for Main WH, Store B, Store C. Review and post them to load the stock." with [Open drafts].

Row error messages (exact):

| Case | Text |
|---|---|
| SKU empty | `Row {row}, column SKU: SKU is required.` |
| Duplicate in file | `Row {row}, column SKU: "{sku}" also appears in row {other}.` |
| Barcode used by another product | `Row {row}, column Barcode: "{code}" already belongs to {sku}.` |
| Category not found | `Row {row}, column Category: "{value}" doesn't exist. Add it first or fix the name.` |
| Bad number | `Row {row}, column {column}: "{value}" is not a number.` |
| Negative qty (opening stock) | `Row {row}, column Quantity: must be 0 or more.` |
| Unknown warehouse (opening stock) | `Row {row}, column Warehouse: "{value}" isn't a warehouse code or name.` |
| Deactivated product (opening stock) | `Row {row}, column SKU: "{sku}" is deactivated.` |

### 3.7 Stock enquiry (everyone with Stock)

1. Menu Stock (or Home search). (S20) opens with the user's warehouse(s), sorted by product name, search focused on desktop (not on phone, to avoid the keyboard hiding results).
2. Type SKU/name/barcode (debounced, 300ms) and/or pick Warehouse and Category; toggle "Low stock only".
3. Result row: product, SKU, warehouse, on-hand big, unit, "Low" chip when on-hand <= safety stock.
4. Tap a row -> (S21): header with totals per warehouse, then ledger table (date, document link, type, qty change, balance after, user). Document link opens the source document read-only. No edit or delete exists anywhere on this screen (STK-02).
5. [Export Excel] in the header for Admin, Owner, Supervisor; exports the current filter. Hidden for Warehouse staff and Clerk.

### 3.8 Low-stock list

1. Home tile "Low stock (n)" or menu item (badge shows n). (S22) lists product x warehouse where on-hand <= safety stock, worst first (largest shortfall percentage).
2. Row: product, warehouse, on-hand, safety stock, short by. Tap -> S21.
3. Roles that can create receipts see [Create receipt] on the row: opens S11 new with product and warehouse prefilled and quantity focused. Others see no button.
4. A product leaves the list when a posted receipt brings it above safety stock; the list needs no manual "mark handled" in MVP (alert frequency, email and push are Phase 2).

### 3.9 Receipt-issue detail (S40 tab, `GET /api/reports/movements` and its Excel export)

One row per line of a posted document, newest movement first (the order is part of the contract, see `docs/architecture/data-and-performance-rules.md` section 8). Columns: posted (MYT), document, type, status, warehouse, partner, SKU, product, qty, unit, base qty, **stock effect**, unit cost, amount.

- `qty` and `baseQty` are unsigned, as entered and in base units. **`stockEffect`** (Excel "Stock effect") is signed and always in base units: positive when the movement adds stock, negative when it removes it. A receipt adds, an issue removes. Adding the effects of a product over a period gives the change in its stock balance, so a user can filter the export and sum the column.
- A reversed document appears twice per line: the posting (status Reversed, effect as at posting) and a **reversal row** (`isReversal = true`, the opposite sign: a reversed receipt shows negative, a reversed issue positive). The reversal row is dated by the time of the reversal (`postedAt` carries that time), and the date range filters it by that time, so a period always adds up to its own change.
- A transfer will show as two movements (out negative, in positive) once it exists as a document type; today a transfer is an issue plus a receipt.

## 4. Wireframes

Conventions: `[Button]`, `( )` radio, `[x]` checked, `<v>` dropdown, `*` required, `(!)` error, `~~~` skeleton, `[CHIP]` status chip. Phone frames are 360px (about 38 columns). Desktop frames are 1280px (about 100 columns). Numbers in `(1)` refer to the notes below each frame.

### 4.1 Login (S01)

Phone:

```
+------------------------------------+
|                                    |
|              [ IMS ]               |
|        Inventory Management        |
|                                    |
|  Email or username *               |
|  +------------------------------+  |
|  | siti@acme.my                 |  |
|  +------------------------------+  |
|  Password *                        |
|  +----------------------+-------+  |
|  | ************         | Show  |  |
|  +----------------------+-------+  |
|                                    |
|  +------------------------------+  |
|  |           Sign in            |  |
|  +------------------------------+  |
|                                    |
|  Language: English <v>             |
|                                    |
+------------------------------------+
```

Desktop:

```
+----------------------------------------------------------------------------------------------------+
|                                                                                                    |
|                                                                                                    |
|                                   [ IMS ]  Inventory Management                                    |
|                                                                                                    |
|                            +----------------------------------------------+                        |
|                            |  Sign in                                     |                        |
|                            |                                              |                        |
|                            |  Email or username *                         |                        |
|                            |  [                                        ]  |                        |
|                            |  Password *                                  |                        |
|                            |  [                                 ] [Show]  |                        |
|                            |                                              |                        |
|                            |  [               Sign in                  ]  |                        |
|                            |                                              |                        |
|                            |  Language: English <v>                       |                        |
|                            +----------------------------------------------+                        |
|                                                                                                    |
+----------------------------------------------------------------------------------------------------+
```

Notes: (1) Show/Hide password toggle (no typing blind on a phone). (2) Wrong password and lockout use the same placement: a red banner above the Sign in button (E7); never name which field was wrong. (3) Enter submits; button shows spinner and is disabled while signing in. (4) No "forgot password" in MVP (not in PRD); Admin resets passwords in S50. (5) After session expiry the user lands here with "Your session expired. Sign in again." and returns to the page they were on.

### 4.2 Document list (S10 Receipts, S12 Issues share the layout)

Phone:

```
+------------------------------------+
| Receipts                    (user) |
+------------------------------------+
| [ Search no., supplier, ref...   ] |
| [Mine] [All]  [Status: Any <v>]    |
+------------------------------------+
| RCV-2610-0007            [DRAFT]   |
| Acme Trading                       |
| Main WH | 3 lines | 120 PCS        |
| 09 Oct 2026 14:05                  |
+------------------------------------+
| RCV-2610-0006      [RETURNED] [D]  |
| Best Supply                        |
| Main WH | 1 line | 40 PCS          |
| 09 Oct 2026 11:20                  |
+------------------------------------+
| RCV-2610-0005          [POSTED]    |
| Acme Trading                       |
| Main WH | 5 lines | 640 PCS        |
| 08 Oct 2026 16:41                  |
+------------------------------------+
|                          [ + New ] |
+------------------------------------+
|Home|Receive|Issue|Stock|More       |
+------------------------------------+
```

Desktop:

```
+-------------------------------------------------------------------------------------------------------+
| IMS      | Receipts                                                  [Export]   [ + New receipt ]     |
|----------+--------------------------------------------------------------------------------------------|
| Home     | [ Search no., supplier, reference...      ]  Warehouse <v>  Status <v>  Date <v>  [Mine]   |
| Receipts |-----------------------------------------------------------------------------------------   |
| Issues   | No.          Date          Warehouse  Supplier       Ref       Lines  Qty   Status         |
| Approvals|-----------------------------------------------------------------------------------------   |
| Stock    | RCV-2610-0007 09 Oct 14:05 Main WH    Acme Trading   DO-5521     3    120   [DRAFT]        |
| Low stock| RCV-2610-0006 09 Oct 11:20 Main WH    Best Supply    DO-0098     1     40   [DRAFT][Returned] |
| Products | RCV-2610-0005 08 Oct 16:41 Main WH    Acme Trading   DO-5519     5    640   [POSTED]       |
| Partners | RCV-2610-0004 08 Oct 10:02 Store B    Zeta Pte       --          2     18   [CANCELLED]    |
| Reports  |-----------------------------------------------------------------------------------------   |
|          | 1-25 of 142                                                         < 1 2 3 4 5 6 >        |
+-------------------------------------------------------------------------------------------------------+
```

Notes: (1) Default filter = Mine for Warehouse staff and Clerk, All for others; the choice is remembered per user. (2) Cards show the 4 facts staff look for: number, other party, where and how much, when. (3) "Returned" is a badge on a Draft, not a status. (4) [ + New ] is the only primary action; hidden for Owner (read-only). (5) Issue list swaps Supplier for Customer and adds a Purpose column. (6) Sorted newest first; paging 25.

### 4.3 Document edit - Issue draft, phone, with insufficient stock (S13)

```
+------------------------------------+
| <  Issue ISS-2610-0012      [DRAFT]|
+------------------------------------+
| (!) Returned by Raj: Wrong         |
|     customer on line 2.            |
+------------------------------------+
| Warehouse *          Main WH <v>   |
| Purpose *   (o)Sale ( )Use ( )Scrap|
| Customer             Delta Co. <v> |
| Reference            [ SO-8812   ] |
| Remark               [           ] |
+------------------------------------+
| Items (2)                          |
| +--------------------------------+ |
| | WIDGET-A  Widget A blue        | |
| | Qty  [ - ]  [ 30 ]  [ + ]  PCS | |
| | (!) Only 12 PCS available in   | |
| |     Main WH.  [Set to 12]      | |
| |                       [Remove] | |
| +--------------------------------+ |
| | BOLT-M8   Bolt M8 x 40         | |
| | Qty  [ - ]  [ 100 ]  [ + ] PCS | |
| | 540 available                  | |
| |                       [Remove] | |
| +--------------------------------+ |
| [ + Add item ]                     |
+------------------------------------+
| 1 line short - fix to submit       |
| [ Save draft ]   [ Submit & post ] |
+------------------------------------+
```

Notes: (1) Footer is sticky; the primary button is bottom right under the thumb. With a short line the primary is disabled and the line above it says why. (2) Returned banner shows reviewer name and reason; it is the first thing on screen. (3) Available stock is shown on every line, red text only when short (plus icon and sentence, not colour alone). (4) With negative stock allowed the red box becomes amber: "Short by 18 PCS. A supervisor must approve this issue." and the primary becomes [Submit] (enabled). (5) Header fields collapse to a one-line summary once there is at least one line, to keep lines on screen; tap to expand.

Add-item bottom sheet (opens from [ + Add item ]):

```
+------------------------------------+
| Add item                     [ X ] |
+------------------------------------+
| [ widg                           ] |
|------------------------------------|
| WIDGET-A  Widget A blue            |
|           12 PCS available         |
| WIDGET-B  Widget B red             |
|           0 PCS available          |
|------------------------------------|
| Quantity                           |
| [ - ]   [ 12 ]   [ + ]       PCS   |
| (numeric keypad)                   |
|                                    |
| [ Add and next ]       [ Add ]     |
+------------------------------------+
```

Receipt variant: same layout; "Purpose" and "available" lines are replaced by Supplier and an optional "Unit cost" field in the sheet ("More" disclosure on phone), shown only when the user has `cost.view` (the Warehouse staff never see it, see 1.4). A deactivated product never appears in results.

### 4.4 Document edit / view - Receipt, desktop (S11)

Draft:

```
+--------------------------------------------------------------------------------------------------------+
| IMS      | Receipts > RCV-2610-0007  [DRAFT]                    [Cancel document]  [Save draft]        |
|          |                                                      [Submit & post]                        |
|----------+---------------------------------------------------------------------------------------------|
| Menu     | Warehouse *  Main WH <v>      Supplier  Acme Trading <v>     Date  09 Oct 2026              |
|          | Reference    [ DO-5521     ]   Remark    [                                  ]               |
|          |-----------------------------------------------------------------------------------------    |
|          | Items                                                                                       |
|          |  #  Product (SKU / name)            Qty        Unit   Unit cost    Remark                 |
|          |  1  [ WIDGET-A  Widget A blue  ]  [ 40   ]    PCS    [ 2.50  ]    [          ]  [x]         |
|          |  2  [ BOLT-M8   Bolt M8 x 40   ]  [ 80   ]    PCS    [       ]    [          ]  [x]         |
|          |  3  [ type to add...           ]                                                            |
|          |                                              Total qty: 120     Lines: 2                    |
+--------------------------------------------------------------------------------------------------------+
```

Posted (read-only):

```
+------------------------------------------------------------------------------------------------------+
| IMS      | Receipts > RCV-2610-0005  [POSTED]                                       [Reverse]        |
|----------+-------------------------------------------------------------------------------------------|
| Menu     | Warehouse  Main WH     Supplier  Acme Trading   Date  08 Oct 2026   Ref DO-5519           |
|          | Created by Ali 08 Oct 15:58 | Submitted 16:10 | Posted by Siti 08 Oct 16:41               |
|          |-----------------------------------------------------------------------------------------  |
|          | [ Items ] [ History ] [ Stock ledger entries ]                                            |
|          |  #  SKU        Product              Qty     Unit                                          |
|          |  1  WIDGET-A   Widget A blue        400     PCS                                           |
|          |  2  BOLT-M8    Bolt M8 x 40         240     PCS                                           |
+------------------------------------------------------------------------------------------------------+
```

Notes: (1) Tab/Enter moves through the line grid; the last empty row is always "type to add" so a desk user never leaves the keyboard. (2) Action buttons come from `allowedActions` (section 7.2); a button that is not allowed is absent, except Approve, which is shown disabled with a reason when the only blocker is self-approval. (3) Posted: every input becomes plain text; History tab lists status changes with user, time, and reasons (reject, override, reverse); this reads from the audit log. (4) Reversed shows an info block under the header: "Reversed by {name} on {date}. Reason: {reason}". (5) Cancelled shows the same read-only layout with chip Cancelled and the optional cancel reason.

### 4.5 Approver view - Issue with override, desktop (S13 read, Submitted)

```
+------------------------------------------------------------------------------------------------------+
| IMS      | Issues > ISS-2610-0012  [SUBMITTED]  [OVERRIDE NEEDED]      [Reject]   [Approve]          |
|----------+-------------------------------------------------------------------------------------------|
| Menu     | Submitted by Ali, 09 Oct 2026 14:12 | Main WH | Sale | Delta Co. | Ref SO-8812            |
|          |-----------------------------------------------------------------------------------------  |
|          | Stock check                                                                               |
|          |  SKU       Product           Requested  Available  Balance after   Value                  |
|          |  WIDGET-A  Widget A blue         30         12         -18  (!)                           |
|          |  BOLT-M8   Bolt M8 x 40         100        540         440                                |
+------------------------------------------------------------------------------------------------------+
```

Approve opens the dialog:

```
+------------------------------------------+
| Approve with stock override              |
| WIDGET-A will go to -18 PCS.             |
| Reason for override (required)           |
| [                                      ] |
|                                          |
|                    [ Back ]  [ Approve ] |
+------------------------------------------+
```

### 4.6 Stock enquiry (S20)

Phone:

```
+------------------------------------+
| Stock                       (user) |
+------------------------------------+
| [ Search SKU, name or barcode    ] |
| Warehouse: Main WH <v>             |
| [Category <v>]  [ ] Low stock only |
+------------------------------------+
| Widget A blue                      |
| WIDGET-A  | Main WH                |
|                      12 PCS  [LOW] |
+------------------------------------+
| Bolt M8 x 40                       |
| BOLT-M8   | Main WH                |
|                     540 PCS        |
+------------------------------------+
| Washer 8mm                         |
| WASH-8    | Main WH                |
|                       0 PCS  [LOW] |
+------------------------------------+
|Home|Receive|Issue|Stock|More       |
+------------------------------------+
```

Desktop:

```
+-------------------------------------------------------------------------------------------------------+
| IMS      | Stock                                                              [ Export Excel ]        |
|----------+--------------------------------------------------------------------------------------------|
| Menu     | [ Search SKU, name or barcode      ]  Warehouse <v>  Category <v>  [ ] Low stock only      |
|          |-----------------------------------------------------------------------------------------   |
|          | SKU        Product              Category   Warehouse   On hand   Unit   Safety   Status    |
|          |-----------------------------------------------------------------------------------------   |
|          | WIDGET-A   Widget A blue        Widgets    Main WH         12   PCS       50    [LOW]      |
|          | BOLT-M8    Bolt M8 x 40         Fasteners  Main WH        540   PCS      200               |
|          | BOLT-M8    Bolt M8 x 40         Fasteners  Store B         60   PCS      200    [LOW]      |
|          | WASH-8     Washer 8mm           Fasteners  Main WH          0   PCS      100    [LOW]      |
|          |-----------------------------------------------------------------------------------------   |
|          | 1-25 of 312                                                         < 1 2 3 4 5 6 >        |
+-------------------------------------------------------------------------------------------------------+
```

Notes: (1) Phone cards deliberately show only name, SKU, warehouse, on-hand and Low; safety stock and category are on S21. (2) On-hand is the largest text on the card (the one number staff want). (3) "All warehouses" yields one row per product per warehouse (Phase 2 adds In Transit). (4) Warehouse staff with one warehouse do not see the Warehouse filter. (5) Export button is absent for Warehouse staff and Clerk.

### 4.7 Approvals inbox (S14)

Phone:

```
+------------------------------------+
| Approvals                   (user) |
+------------------------------------+
| [Waiting for me 3] [Mine]          |
| Type <v>   Warehouse <v>           |
+------------------------------------+
| ISS-2610-0012       [OVERRIDE]     |
| Issue | Ali | Main WH              |
| 2 lines | 130 PCS                  |
| Waiting 2 h                        |
+------------------------------------+
| RCV-2610-0009        [LARGE]       |
| Receipt | Ali | Main WH            |
| 12 lines | 4,800 PCS               |
| Waiting 1 d                        |
+------------------------------------+
|Home|Approvals|Stock|Docs|More      |
+------------------------------------+
```

Desktop:

```
+------------------------------------------------------------------------------------------------------+
| IMS      | Approvals                                                                                 |
|----------+-------------------------------------------------------------------------------------------|
| Menu     | [ Waiting for me (3) ] [ Submitted by me ]      Type <v>  Warehouse <v>                   |
|          |-----------------------------------------------------------------------------------------  |
|          | No.            Type     By    Warehouse  Lines  Qty    Flags         Waiting              |
|          |-----------------------------------------------------------------------------------------  |
|          | ISS-2610-0012  Issue    Ali   Main WH       2    130   [OVERRIDE]       2 h   [ Review ]  |
|          | RCV-2610-0009  Receipt  Ali   Main WH      12   4800   [LARGE]          1 d   [ Review ]  |
|          | ISS-2610-0010  Issue    Mei   Store B       1     20                    3 d   [ Review ]  |
|          |-----------------------------------------------------------------------------------------  |
|          | Oldest first                                                                              |
+------------------------------------------------------------------------------------------------------+
```

Notes: (1) Sorted oldest first so nothing ages out. (2) "Submitted by me" lists my own Submitted documents (and shows why I cannot approve them: "You submitted this"); Owner sees only LARGE items in tab 1. (3) Approve is never done from the row: the approver must see lines first (error prevention); Review opens the document (4.5). (4) After Approve or Reject on phone, the app returns to this list. (5) Empty state: "Nothing waiting for your approval."

## 5. Wireframe coverage check

| Required | Phone | Desktop |
|---|---|---|
| Login | 4.1 | 4.1 |
| Document list | 4.2 | 4.2 |
| Document edit (receipt/issue) | 4.3 (issue, shortfall) + add-item sheet | 4.4 (receipt draft + posted), 4.5 (approver, override) |
| Stock enquiry | 4.6 | 4.6 |
| Approvals inbox | 4.7 | 4.7 |

## 6. States and copy

### 6.1 Loading

| Situation | Treatment |
|---|---|
| List first load | Skeleton: 5 grey rows/cards after 300ms (nothing flashes for fast responses). Filters stay usable. |
| List refetch (filter change) | Keep old rows at 60% opacity, thin progress bar under the header. |
| Document open | Header skeleton + 3 line skeletons. |
| Button action (save, submit, post, approve) | Button shows spinner and is disabled; the rest of the form is disabled to stop edits during the call. |
| Slow request (> 8 s) | Inline text under the spinner: "Still working. Don't close this page." |
| Export / import parse | Progress dialog with the filename; cancel allowed for parse. |

### 6.2 Empty states (icon + 1 line + 1 action)

| Screen | Line | Action |
|---|---|---|
| Receipts (Mine, none) | "No receipts yet." | [New receipt] (hidden for Owner; Owner text: "No receipts to show.") |
| Receipts with filter, none | "No receipts match these filters." | [Clear filters] |
| Issues (none) | "No issues yet." | [New issue] |
| Approvals, Waiting for me | "Nothing waiting for your approval." | none |
| Approvals, Submitted by me | "You have nothing submitted." | none |
| Stock, search none | "No product matches \"{query}\"." | [Clear search] |
| Stock, no stock rows at all | "No stock yet. Post a receipt to add stock." | [New receipt] if allowed |
| Low stock (none) | "Nothing is low on stock." | none |
| Products (none) | "No products yet. Add one or import a file." | [Add product] [Import] |
| Product ledger (none) | "No stock movements for this product." | none |
| Reports, no rows | "No data for these filters." | [Change filters] |
| Audit log, no rows | "No activity for these filters." | [Clear filters] |
| Warehouse staff with no warehouse assigned | "You aren't assigned to a warehouse yet. Ask an Admin." | none (shown instead of Home content) |

### 6.3 Error handling patterns

| Error class | Where shown | Behaviour |
|---|---|---|
| Field validation (400 with field errors) | Under the field; first invalid field focused | No toast. |
| Business rule violation (409/422: stock, state, self-approval, reversal) | Inline on the affected line or document banner, plus an error toast with the same text | Document state unchanged; message says what to do. |
| Stale document (409 row-version) | Banner at top of document with [Reload] | Edits are not saved; see E4. |
| Forbidden (403) | Full page S03 for routes; toast for actions | See E8. |
| Unauthenticated / token expired | Redirect to S01 with message and return URL | Auto-refresh first (AUTH-01); only redirect if refresh fails. |
| Network down / 5xx | Toast "Can't reach the server. Check your connection and try again." with [Retry] | Form data kept. List screens show an error panel with [Retry]. |
| Not found (404) | S03 | "This page or document doesn't exist or was removed." [Go to Home] |

### 6.4 The 8 most important error messages (exact copy)

Tone: say what happened, then what to do. No "Oops", no codes shown to users (code kept in the details disclosure for support). `{...}` are placeholders.

| ID | Trigger | Where | Copy |
|---|---|---|---|
| E1 | Issue line quantity exceeds available stock, negative stock not allowed | Inline on the line, plus footer note | Line: "Only {available} {uom} available in {warehouse}." Action: [Set to {available}]. Footer: "{count, plural, one {1 line is short on stock} other {# lines are short on stock}}. Fix them to submit." |
| E2 | Insufficient stock and negative stock allowed (override path) | Amber note on the line; dialog on Submit | Line: "Short by {short} {uom}. A supervisor must approve this issue." Dialog title: "Send for supervisor override?" Body: "{count, plural, one {1 line goes} other {# lines go}} below zero stock. A supervisor will review it." Buttons: [Back] [Send]. |
| E3 | Submitter (or creator) tries to approve own document | Disabled Approve button with text under it; toast if forced | "You submitted this document, so you can't approve it. Ask another supervisor or an Admin." |
| E4 | Save/submit/post with an outdated RowVersion | Banner on the document + error toast | "{name} changed this document at {time}. Reload to see the latest version. Your changes were not saved." Button: [Reload] |
| E5 | Post (or submit) fails because stock moved since the draft | Toast + the affected lines marked | "Can't post {docNo}. Stock for {sku} is now {available} {uom}, but the document needs {requested}. Open the document and adjust it." Buttons: [Open document] |
| E6 | Reverse would drive stock below zero | Inside the reverse dialog, replaces body | "Can't reverse {docNo}. It would make {sku} negative ({balance} {uom}). Reverse or adjust the later issues first, or ask an Admin." (When several products: "...would make {count} products negative: {sku list}.") |
| E7 | Login failure; lockout | Banner above Sign in | Wrong: "Email, username or password is wrong." Locked (5 failures): "Too many failed attempts. Try again in {minutes} minutes." Never state which of the two fields was wrong or whether the account exists. |
| E8 | 403 on route or action | S03 page / toast | Page title: "You don't have access to this." Body: "Your role ({role}) can't open this. Ask an Admin if you need it." Button: [Go to Home]. Toast version: "You don't have permission to do that." |

Also needed (shorter, same tone):

| Trigger | Copy |
|---|---|
| Duplicate SKU | "SKU {sku} already exists." |
| Barcode already used | "Barcode {code} already belongs to {sku}." |
| Product deactivated on a new document | "{sku} is deactivated and can't be added." |
| Deactivating a product with stock | "{sku} still has {qty} {uom} in stock. You can deactivate it, but you can't use it on new documents." (confirm dialog, not an error) |
| Import wrong file type / size | "Upload an .xlsx file up to 5 MB. Download the template if you need the format." |
| Import has errors | "{count, plural, one {1 row has an error} other {# rows have errors}}. Nothing was imported. Fix your file and upload it again." |
| Reason too short (reject, override, reverse) | "Enter a reason of at least 5 characters." |
| Session expired | "Your session expired. Sign in again." |
| Network | "Can't reach the server. Check your connection and try again." |
| Export failed | "Couldn't create the file. Try again." |

Success toasts: "Draft saved." / "Submitted for approval." / "Sent for approval: quantity reaches {threshold}." / "Posted. Stock updated." / "Approved." / "Returned to {name} for changes." / "Reversed. Stock adjusted." / "Document cancelled." / "{count} products added, {updated} updated."

Dialog copy:

| Dialog | Title | Body | Reason | Buttons |
|---|---|---|---|---|
| Post | Post {docNo}? | Stock will {increase\|decrease} for {count} lines. Posted documents can't be edited, only reversed. | -- | [Back] [Post] |
| Approve | Approve {docNo}? | The document can then be posted. | -- (required when override: "Reason for override") | [Back] [Approve] |
| Reject | Reject {docNo}? | It goes back to {name} as a draft with your reason. | required | [Back] [Reject] |
| Cancel document | Cancel {docNo}? | This can't be undone. | optional | [Keep document] [Cancel document] |
| Reverse | Reverse {docNo}? | This adds opposite stock entries. The original stays on record. | required | [Back] [Reverse] |
| Leave with unsaved changes | Discard changes? | You have unsaved changes. | -- | [Keep editing] [Discard] |

Destructive-style buttons: Reject, Cancel document, Reverse, Discard.

## 7. Status chips and actions

### 7.1 Status chips

Colour tokens are named for fe-designer to define (light and dark); each chip has an icon and a text label so it still reads in poor light or for colour-blind users. Contrast AA for label on chip.

| Status | Chip text | Token | Icon | Meaning shown under/near chip (detail screen only) |
|---|---|---|---|---|
| Draft | Draft | `--status-draft` (grey) | pencil | -- |
| Submitted | Submitted | `--status-submitted` (blue) | clock | "Awaiting approval" if `requiresApproval`, else "Ready to post" |
| Approved | Approved | `--status-approved` (teal) | check | "Approved by {name}" |
| Posted | Posted | `--status-posted` (green) | check-double | "Posted by {name} on {date}" |
| Reversed | Reversed | `--status-reversed` (amber) | undo | "Reversed by {name}. Reason: {reason}" |
| Cancelled | Cancelled | `--status-cancelled` (grey, strike-through label) | x | "Cancelled by {name}" |

Flag badges (not statuses; outlined style, shown beside the chip): `Returned` (Draft after a reject, amber outline, hover/tap shows the reason), `Override needed` (Submitted, red outline), `Large` (Submitted and above threshold, blue outline).

### 7.2 Action buttons per status and role

This table is the spec for `allowedActions[]` (D3). "Own" = the document's creator. "Submitter" = the user who submitted it. All actions also require the document's warehouse to be in the user's scope. A role not listed in a cell sees no action buttons (view only). Buttons are named exactly as shown.

Primary button = the first listed; at most one primary and one secondary appear in the bar (phone: sticky bottom bar; desktop: header right). The rest go into the overflow menu `...`.

| Status | Admin | Owner | Supervisor | Warehouse staff | Clerk |
|---|---|---|---|---|---|
| **Draft** | Save draft, **Submit & post**, Cancel document, Edit (any) | view only | Save draft, **Submit & post**, Cancel document, Edit (any) | Own only: Save draft, **Submit & post**, Cancel document | Own only: Save draft, **Submit**, Cancel document |
| **Submitted** (approval required) | **Approve**, Reject, Cancel document. Approve disabled if Admin is the submitter | **Approve**, Reject only if flagged Large and Owner is not the submitter; otherwise view only | **Approve**, Reject, Cancel document. Approve disabled if Supervisor is the submitter | Submitter only: Cancel document (recall) | Submitter only: Cancel document (recall) |
| **Submitted** (no approval required) | **Post**, Cancel document | view only | **Post**, Cancel document | **Post** (any in scope), Cancel document if submitter | Cancel document if submitter |
| **Approved** | **Post** | view only | **Post** | view only (Q2) | view only |
| **Posted** | **Reverse** | view only | **Reverse** (reason required) | view only | view only |
| **Reversed** | view only | view only | view only | view only | view only |
| **Cancelled** | view only | view only | view only | view only | view only |

Extras that apply to every non-Draft status for roles that can create: overflow action "Duplicate" opens a new Draft pre-filled with the same lines (optional, cheap; cut if schedule is tight). Print is Phase 2 and not rendered. "View stock ledger entries" link shows on Posted and Reversed for roles with Stock access.

Rules behind the table (for be-framework to enforce; UI only reflects them):

- Reject returns the document to Draft; creator becomes editable again. Reason required.
- Approve, Reject, and Cancel on Submitted are blocked for the submitter when self-approval is disabled (default). Cancel by the submitter stays allowed.
- Post on Submitted requires `requiresApproval = false` for Warehouse staff; Admin and Supervisor may post Submitted only when no approval is required, and Approved always.
- Reverse needs Posted status and a reason; E6 applies.
- A document with `requiresOverride` needs Approve with a reason before Post.
- Reasons (Reject, Reverse, override at Approve) have at least 5 characters once trimmed; the API enforces it as well as the dialog (PS-09).
- Review routing (enforced by the API, shown by `allowedActions` and `canAct`): an Admin approves and rejects any document; the Owner only documents flagged Large; a Supervisor only documents not flagged Large. A document with `requiresOverride` that is not Large is therefore a Supervisor or Admin act, never the Owner's. Reject is a review like Approve, so the submitter cannot reject their own document either when self-approval is off (the recall is Cancel). **Open question for the product owner:** a document that is both Large and `requiresOverride` is reviewed by the Owner (or Admin), who then gives the override reason; D5 calls the override "a supervisor act", so say if that case should be Admin-only.
- Ownership: Warehouse staff and Clerk edit, submit and cancel only drafts they created, and cancel (recall) only Submitted documents they submitted. A draft that a supervisor submitted can no longer be recalled by its creator. Supervisor and Admin act on any document in their warehouses. A document in a warehouse outside the caller's scope answers 404, not 403, so ids cannot be probed.
- Amounts: an issue that has not posted yet is valued at the average cost seen when it was last saved or submitted (`amountIsEstimate`); the value on a Submitted document is the one the approval rules were tested against, and `approvalTriggers` says which threshold (value or quantity) it reached. Every amount here is `null` for a caller without `cost.view` (the Warehouse staff): they still see that approval is needed and which kind of rule fired, not the number (1.4).

### 7.3 Phone action bar by state (what the sticky bar shows)

| State (role) | Bar |
|---|---|
| Draft (creator) | `[Save draft] [Submit & post]` (Clerk: `[Save draft] [Submit]`); Cancel document in `...` |
| Draft, Returned (creator) | same, banner above |
| Submitted, approval needed (approver) | `[Reject] [Approve]` |
| Submitted, approval needed (submitter) | `[Cancel document]` as secondary; text "Waiting for approval" |
| Submitted or Approved, can post | `[Post]` |
| Posted (Supervisor/Admin) | `[Reverse]` (secondary danger style) |
| Everything else | no bar; read-only content fills the screen |

## 8. Traceability and handoff hooks

| Requirement | Where covered |
|---|---|
| AUTH-01 | 4.1, E7, 6.3 |
| AUTH-02 | 1.2 menus, 7.2, E8 |
| MD-01..03, MD-05 | S30-S34, 3.6 |
| IN-01, IN-02 | 3.1, 4.3, 4.4 |
| OUT-01, OUT-02, OUT-03 | 3.2, 3.3, 4.3, 4.5, E1-E3 |
| BR-02 (reversal) | 3.5, E6 |
| BR-07 (no self-approval) | 3.3, E3, 7.2 |
| BR-10 (time zone) | D10 |
| BR-11 (numbering) | 4.2, Q5 |
| BR-12 (concurrency) | E4, E5 |
| STK-01, STK-02 | 3.7, 4.6, S21 |
| Low-stock list (T16) | 3.8, S22 |
| RPT-01 | S40 (Export Excel on every report) |

API needs surfaced by this design (for fe-architect to put in the contract): `allowedActions[]` on document; document fields `requiresApproval`, `requiresOverride`, `isLarge`, `returnedReason`, `returnedBy`, `overrideReason`; per-line `available` for issues in the picker and on the document; submit response `{ status, requiresApproval, reason }`; import preview response `{ summary, rows[{row,status,errors[]}] }` plus an error-list download endpoint; stock list endpoint with `isLow`; counts for Home and menu badges (`approvalsWaiting`, `lowStockCount`, `returnedToMe`, `myDrafts`).
