# QA retest: first-day warehouse staff on a 360px phone, after the fix round

Persona: no training, in a hurry, one thumb. User: demo `staff` (Warehouse staff, one warehouse "Main warehouse"). Date of run: 2026-10-11. Build: current main, web http://localhost:4200 (production build), API http://localhost:5109, throw-away QA database. Previous report: `docs/qa/novice-warehouse-staff.md` (NV-01..NV-28). Read first: `docs/qa/fix-status-frontend.md`, `docs/qa/fix-status-backend.md`, `docs/qa/retest-power-supervisor.md` and `docs/qa/retest-edge-accessibility.md` (PR-01..PR-14 and ER-01..ER-21 are not repeated here, only referenced), `docs/design/mvp-ux.md` (flows, E1 to E8, 1.4), `docs/design/2026-refresh-plan.md` (what is still planned).

Result: of the 28 old findings 21 are fixed, 4 are still open (each one partly fixed), 1 is not reproducible, 2 need a product decision, 0 regressed. The Blocker NV-01 and the Major NV-02 are fixed and held up under every try (save, submit, Back in the Post dialog, double tap, reload). New findings: 0 Blocker, 2 Major, 9 Minor, 7 Polish (18 total). The two Majors are about mistakes: typing a quantity into a field that already holds 1 gives 1xx (NR-01), and a wrong quantity noticed in the Post dialog can only be fixed by cancelling the whole document (NR-02). Staff see no cost or value anywhere (checked on 15 screens and in the copy, one word leak in NR-09).

## Method and limits

- Browser: my own headless Edge (Chromium 155) driven through the DevTools protocol, viewport 360x740, device scale 2, touch emulation on, English first, then zh and ms. The shared pane was not used. Taps are mouse events at element centres (no real finger, no real touch scroll). Tap sizes measured with `getBoundingClientRect` on 14 screens; overflow with `scrollWidth`; network and console read per run. Full page loads were kept under the 20 a minute limit (about 40 loads and 3 sign-ins in the session, no 429).
- On-screen keyboard: not available. Approximation: viewport 360x440 (the layout viewport an Android keyboard leaves). At that height the top bar and tab bar become part of the page (EA-04 design), the focused quantity and remark field stay in view, the Add item sheet keeps Done reachable and its list scrolls. Real keyboard overlap is not proven.
- Offline: the emulation flag had no effect on localhost (requests still went through), so offline behaviour was not tested. Real camera scanning: not tested (Phase 2).
- Login lockout: I typed one wrong password (as allowed) and did not provoke the lockout; the lockout sentence was already verified by the edge re-test (EA-13, fixed).
- Data written: receipts RCV-WH01-2610-0372 to 0377 (two posted: 13 and 150 units), a few drafts, one Submitted receipt, ISS-0011 (submitted, post refused, cancelled), ISS-0012 (supervisor, 201 x DEMO-002, made through the API to cause the stock change), ISS-0013 (awaiting approval). DEMO-002 now has 21 on hand. No screenshots or credentials are stored in the repo.

## 1. Old findings NV-01..NV-28

| ID | Old sev | Status | Evidence in this run |
|---|---|---|---|
| NV-01 save draft: false discard dialog, duplicate drafts | Blocker | fixed | New receipt, two lines, Save draft: one `POST /api/documents/receipts`, URL moves to `/receipts/387`, toast "Draft saved.", no dialog, the number RCV-...-0372 and chip Draft appear. Also checked: Save draft with a one-line form from the Low stock shortcut, and Submit & post on a new form (one POST + one submit, the Post dialog opens on the saved document). Double tap 100 ms apart on a new form: one POST. |
| NV-02 Back in the Post dialog, then Submit & post again, second document | Major | fixed | Issue and receipt: Submit & post creates the document and the URL moves to `/issues/388` before the dialog is answered. Back lands on the Submitted document ("Ready to post") with a Post button and no Submit & post, so no second document can be made (writes: one create, one submit). Consequence for mistakes: NR-02. |
| NV-03 Add item search not focused | Major | fixed | Tap Add item, type a barcode at once and Enter: focus is in the search field on open, the line is added, no tap needed. |
| NV-04 double add looks like the first add | Major | fixed (partly) | "DEMO-001: now 2." then "now 3." (live region), the message is cleared when the search text changes. Not done (as announced): highlight of the changed line; selecting the quantity (NR-01 is the practical effect). |
| NV-05 posted document, no way to fix a wrong quantity | Major | fixed | Posted receipt says "Wrong quantity? Ask a supervisor to reverse this document." The optional "Duplicate" is not shipped. |
| NV-06 Post fails (E5) on a read-only submitted issue | Major | fixed | After another user took the stock: "Stock changed. Stock for DEMO-002 is now 1 PCS, but the document needs 2. Cancel this document and create a new one with a smaller quantity." (focus moves to the banner). After Cancel the banner is gone and only "Document cancelled." shows. Small contradictions remain: NR-11. |
| NV-07 reload mid-edit loses work | Major | still open (partly) | A browser "leave site" prompt appears on reload for a dirty new form and a dirty draft (CDP shows a `beforeunload` dialog). Accepting it gives the saved version (qty 1 instead of the typed value): nothing is restored. Session survives the reload (refresh cookie). |
| NV-08 lists: filters and tall cards, no path to drafts | Major | still open (partly) | Fixed: one Filters button ("Filters (1)" when active), Mine/All tabs (Mine is selected by default), cards 112 to 120 px, two lines plus qty, warehouse shown, whole card is the target. Open: Home tiles "My drafts 5" and "Returned to me" are still not tappable (tap leaves the page on Home); no line count on the card; Status is a dropdown not chips. |
| NV-09 quantity and purpose messages | Minor | fixed | -5 and 0: "Enter a quantity above 0."; 99999999999: "Enter 1000000000 or less."; empty: "This field is required."; Submit without Purpose: "Choose a purpose." and focus moves to the Purpose group; Purpose labelled "(required)"; footer total ignores 0 and negative lines. |
| NV-10 footer is a third of the screen, Cancel next to Submit | Minor | still open (partly) | Fixed: Cancel document is in a "More actions" sheet (one tap further, confirm dialog is clear). Open: on every saved Draft the three buttons wrap to two rows, footer 153 px (NR-03); on a Submitted document the footer sits on top of the first item (NR-04). |
| NV-11 stock cards tall, name second | Minor | fixed | Stock card about 126 px: SKU, name + warehouse, large "10 PCS"; "Low" chip with "Safety 100" on the same row. |
| NV-12 document not found | Minor | fixed | `/receipts/99999`: "Page not found. This page or document doesn't exist or was removed." with Go to Home, no Retry. The red toast "Document not found." still shows with it and the tab title still reads "Receipts - IMS" (known, ER-16). |
| NV-13 no Create receipt on Low stock | Minor | fixed | Each Low stock card has "New receipt" (121x48); it opens `/receipts/new?sku=...&warehouseId=1` with the line and the warehouse set and the quantity focused. Practical problem: NR-01. |
| NV-14 two clear buttons | Minor | fixed | Add item and Stock search show one clear button (48x48). |
| NV-15 placeholder cut | Minor | fixed | Add item: "Name, SKU or barcode"; with one character the hint "Type 2 or more letters, or scan a barcode." stays. A new cut placeholder on the Receipts search: NR-14. |
| NV-16 Malay bottom bar | Minor | fixed | "Utama", every tab label fits (scrollWidth equals clientWidth). |
| NV-17 sub-labels | Minor | fixed | "Ready to post", "Awaiting approval" chip, "Posted by Staff on 11 Oct 2026, 10:20", "Submitted by ...". |
| NV-18 Remove without confirm | Minor | fixed | Remove on a line with a typed quantity: "Remove DEMO-002? Its quantity and cost will be lost." with Keep item / Remove (cost word: NR-09). |
| NV-19 Post dialog shows no quantities | Minor | fixed | "Includes DEMO-001 x 12 PCS, DEMO-002 x 1 PCS." and for five lines "... and 2 more". |
| NV-20 plural, disabled contrast | Polish | fixed | "1 line is short on stock. Fix it to submit."; disabled Submit & post is grey with dark text. |
| NV-21 spinner on the wrong button, toast over the footer | Polish | fixed | With 3 s latency the spinner and busy state are on Save draft, Submit & post stays enabled and a tap on it during the save was handled in order (PUT, PUT, submit, no error). Toasts sit under the top bar (they cover the back link and the top of the title for about 4 s, never the buttons). |
| NV-22 negative total, zh unit word | Polish | fixed | No "-4 units"; "1 unit"; zh "1 行 \| 共 150 件". |
| NV-23 tab strip overflow | Polish | fixed | Items / History / Stock ledger end at x=303 of 360, no sideways scroll. |
| NV-24 More badge and name | Polish | fixed | No badge floats over the sheet now (and none on the Low stock row for Staff); the tab button is named "More". |
| NV-25 Esc and Back with the sheet open | Polish | still open (partly) | Esc closes the Add item sheet (when the search box holds text the first Esc clears it, the second closes). Browser Back with a dirty form and the sheet open still shows "Discard changes?" stacked on top of the open sheet. |
| NV-26 console error on every page | Polish | not reproducible | Only the expected 401 of the start-up refresh before sign-in, the 401 of my one wrong password, the expected 404 (`/receipts/99999`) and 409 (stale save). No service-worker error in the production build. |
| NV-27 date format, default tab | Polish | needs product decision | From/To still use the browser format (mm/dd/yyyy here). The list opens on Mine on a clean profile (confirmed). Same decision as PS-15. |
| NV-28 zh "提交并过账" | Polish | needs product decision | Unchanged, waits for the native review. |

Counts: fixed 21 (NV-01, 02, 03, 04, 05, 06, 09, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24), still open 4 (NV-07, 08, 10, 25), not reproducible 1 (NV-26), needs product decision 2 (NV-27, 28), regressed 0.

## 2. First-day tasks

| Task | Result |
|---|---|
| Sign in, empty submit | Pass. Two "This field is required." messages, focus goes to the first field, labels "(required)". Wrong password: "Email, username or password is wrong." and focus goes to the password field. Banner does not clear while typing (NR-06). |
| Session | Pass. Reload keeps the session; sign out goes to `/login`; a deep link while signed out returns to the document after sign-in. |
| Find a product by typing | Pass. "de" lists two products with "N PCS available" on issues, rows 48 px; one character shows the hint; no match: `No product matches "deb".` |
| Scan / type a barcode or SKU | Pass. Barcode and exact SKU plus Enter add the line and keep the field focused; a repeat says "DEMO-001: now 2." |
| Barcode miss | Pass. Grey status line "No product has the code 9999999999.", not an error (no next step: NR-13). |
| Receipt with several lines | Pass. Five lines by SKU + Enter, quantities typed, footer "5 lines \| 150 units", Post dialog lists three lines and "and 2 more", posted. Typing quantities: NR-01. |
| Save draft, come back to it | Pass. Draft opens again from Receipts (Mine, Filters, Status Draft) by tapping anywhere on the card; edit, Save, Submit & post. Home count "My drafts" matches the API (5) but is not tappable. |
| Submit, Post, Back, Post | Pass. Back keeps one Submitted document with Post; Post makes it Posted with "Stock updated." (no next action: NR-12). |
| Issue within stock | Pass. Purpose required message and focus; the 60 x QA-002 issue (needs approval) shows "Sent for approval." and the "Awaiting approval" chip with "Why this needs approval ... Approver: Supervisor." (no amount). |
| Issue below available stock (E1) | Pass. "Only 202 PCS available in Main warehouse." with a "Set to 202" button (one tap fills 202), footer "1 line is short on stock. Fix it to submit.", Submit disabled grey. |
| Stock-changed error on a submitted issue (E5) | Pass with notes (NR-11). |
| Language en / zh / ms | Pass. Switch is instant, keeps the half-filled form and the screen; no overflow on Home, editor, shortfall; "Utama" fits; all new strings are translated. Wording notes: NR-18. |
| Mistakes: wrong quantity | Mixed. Draft: fixable. Submitted: only Cancel and retype (NR-02). Typing over a default: NR-01. |
| Mistakes: double tap | Pass on new documents and on Post/Submit; existing draft Save draft sends two saves (NR-07). |
| Mistakes: Back button | Pass for the editor ("Discard changes?" with Keep editing / Discard); open Add item sheet NV-25. |
| Mistakes: reload mid-edit | Partly (prompt only, NV-07). |
| Cost and value (Staff) | Pass, see section 3. |
| Login states | Pass except NR-06, see section 4. |
| Touch targets, overflow, footer, keyboard | See section 5. |
| Console and network | No unexpected error and no 5xx in about 250 requests; the 409 and 404 were provoked on purpose. |

## 3. Cost and value as Staff (EA-23, mvp-ux 1.4)

Scanned the visible text of 15 screens for money-shaped numbers (digits with two decimals) and the words cost, value, amount, price, RM, total, worth, average: Home, Receipts (Mine and All), Issues, posted receipt (Items, History, Stock ledger tabs), Stock, product page, Low stock, Products, Warehouses, Partners, Categories and units, new receipt, new issue. Nothing found. Specifically: the receipt editor has no unit cost field; the receipts and issues lists show Total qty only; Stock and Low stock show quantities only, no Value column, no Export; Home has no value tile; the "Why this needs approval" box says "Its value reaches an approval limit." without a number. Receipts work by quantity only: a five-line receipt was made, submitted and posted (stock rose, ledger rows appeared). One copy leak: the Remove dialog says "cost" (NR-09). Clerk and Supervisor were not re-checked here (power and edge re-tests cover them).

## 4. Login screen

- Empty submit: both fields show "This field is required.", focus on the first, no banner. Pass.
- Wrong password (once): banner "Email, username or password is wrong.", one `role="alert"`, focus to the password field, no field blamed. Pass.
- Banner clears when editing: Fail. After typing in the username (and again in the password) the banner stays. The plan has it as D3 in slice R02, so this is "not built yet", recorded as NR-06.
- Lockout sentence: not provoked (see limits).
- Required markers: still "(required)" text in the label (login, receipt, issue, product forms); the planned red asterisk plus legend (D4, R02) is not built yet.

## 5. Touch targets, layout, keyboard

- Primary controls are 48 px or more: Receive stock / Issue stock 328x64, tab bar items 72x63, Add item 133x48, Save draft 107x48, Submit & post 138x48, Post / Back in the dialog 65x48 and 68x48, quantity steppers 58x48, quantity field 124x48, Remove 124x48, Set to N 103x48, search 48, Filters 110x48, dialog Close 58x48, Account 58x48, More actions 58x48, list back link 87x44, card rows 112 to 126 px high (whole card tappable on receipts, stock and low stock).
- Under 44 px: the "IMS" brand link in the top bar 61x24 on every screen; the document links in the product ledger 150x40 to 161x40 (not covered by a card); SKU links on the Products list 76 to 81x40 (rows are not checked as whole targets there). Checkboxes (24 px) and radios (18 px) sit inside 44 to 48 px labels, so they pass.
- Sideways scroll at 360: none on any screen with realistic data. One exception with long names: NR-10.
- Sticky chrome: top bar 56 + footer + tab bar 64. Footer is 83 px on a Submitted document, 153 px on a saved Draft (NR-03), 98 to 120 px on a new form. At 360x440 the bars are static and nothing covers the focused field.

## 6. New findings

Severity: Blocker, Major, Minor, Polish. IDs NR-01 to NR-18, sorted by severity.

| ID | Sev | Screen | Steps | Expected | Actual | Suggested fix |
|---|---|---|---|---|---|---|
| NR-01 | Major | Line quantity in the receipt and issue editor; Low stock "New receipt" shortcut | A) Home, Receive stock, Add item, add DEMO-001 (quantity 1), close the sheet, tap the quantity field and type 12. B) Low stock, New receipt on a row (quantity field is focused with 1), type 50. | The typed number replaces the default: 12 and 50. | The caret goes after the existing digit: the field becomes 112, and in case B 150 (also seen with "3" then "12" giving 312). A hurried worker posts ten times too many; the Post dialog lists the number, but it is the one place they will not read. Backspace first is not obvious. | Select the field's content on focus and on tap (`select()` in a focus handler) and call `select()` when the shortcut focuses it; keep `inputmode="decimal"`. Test: tap, type 12, value is 12. |
| NR-02 | Major | Post dialog, Back, Submitted document (NV-02 consequence) | Receipt with a wrong quantity (10 typed as 110). Submit & post. In "Post RCV-...?" read "DEMO-001 x 110 PCS", tap Back. | Back means "not yet": I can fix the quantity. | Back leaves the saved document as Submitted, read-only (no input, no Save). The only action is Cancel document, hidden behind the "..." button, with the warning "This can't be undone", then every line must be typed again. There is no Edit / Return to draft. (API: `allowedActions` for the submitter on a Submitted document is cancel and post.) | Offer "Edit" on the submitter's own Submitted document (the API returns it to Draft), or make Back in the Post dialog return it to Draft. At minimum label the dialog button "Not yet, edit" and show Edit next to Post. Owner: ui-ux-designer (decide), then be-framework and fe-framework. |
| NR-03 | Minor | Saved Draft footer (receipts and issues) | Open any saved draft at 360. | One row: summary, a few buttons (NV-10 fix status says "the footer is one row"). | "..." (More actions), Save draft and Submit & post do not fit: they wrap to two rows, right aligned, footer 153 px (top bar 56 + footer 153 + tab bar 64 = 37% of the screen). The "..." button shows three dots only and has no visible label. | Give the three controls a smaller gap or let Save draft and Submit & post share one row with the menu as an icon in the summary line. |
| NR-04 | Minor | Submitted document | Submit & post, Back (or open a Submitted document). | The items I am about to post are visible next to the Post button. | Header rows (Warehouse, Supplier "-", Remark "-", Created by, Submitted by) fill the first screen; the Items tab begins at the bottom edge, partly under the sticky footer. | Show Items first on phones, collapse empty Supplier and Remark rows, put people and times into one line. |
| NR-05 | Minor | Low stock (phone) | Open Low stock as Staff. | Every number has a label. | The red figure at the top right of each card (99, 50, 15) has no label; it is "Short by" (the cell is flagged phone-end and its label is not drawn). Safety stock is hidden on phones. Card layout varies: on the SKU-001 card the warehouse and "On hand" sit on one line and the button drops to the next. | Draw "Short by 99" (label next to the number), keep the card structure the same for every row. |
| NR-06 | Minor | Login | Wrong password once, then type a character in either field. | The banner clears when a field is edited (refresh plan D3); the required marker is an asterisk with a legend (D4). | The banner stays; labels still read "(required)". Not built yet (slice R02). | Build R02 as planned; test: banner gone after one keystroke. |
| NR-07 | Minor | Save draft on an existing draft | Draft open, change a quantity, tap Save draft twice (60 to 150 ms apart; and as two clicks in the same tick). | One save. | Two `PUT`s are sent (the busy lock only holds after the first answer); with a 60 to 150 ms gap both succeed and two "Draft saved." toasts stack; with the two clicks in the same tick the second PUT answers 409 and the page shows "Someone else changed this document. Reload to see the latest version. Your changes were not saved." next to "Draft saved.", with the buttons locked until Reload (the first save did succeed). On a new document the double tap is already safe. ER-13 in the edge report is the same cause. | Set the busy state synchronously on the first click for update as well as create; ignore a second command while one runs. |
| NR-08 | Minor | Decimal quantity | In a quantity field type 1.5, then press Backspace repeatedly. | Characters are removed one by one. | After the first Backspace the value is "1" (the point goes with the 5) and the caret jumps to the start: further Backspaces do nothing and a typed "2." then "5" gives "2.51". The field cannot be emptied by Backspace once a point was typed. Found with real key events in Chromium; check on a real phone keyboard. | Do not rewrite the field value while the user is typing (write back on blur), or keep the caret position. |
| NR-09 | Minor | Remove-line dialog | As Staff tap Remove on a line with a typed quantity. | No mention of cost (Staff have no cost, rule 1.4). | "Remove DEMO-002? Its quantity and cost will be lost." The only place in the Staff UI that says "cost". | Text without "and cost" for roles without `cost.view`. |
| NR-10 | Minor | Partners (and any list with a long unbroken name) | Open More, Partners with the QA partners "nnnn..." (100 to 300 characters, no spaces). | Name wraps or truncates, page stays 360 wide. | The cards stay but the unbroken name pushes the page to about 2,900 px (the phone shrinks the whole app, text becomes unreadable). The API accepts these names. | `overflow-wrap: anywhere` on card titles and table cells; clamp to two lines. |
| NR-11 | Minor | Stock-changed banner (E5) | Post a submitted issue after another user took the stock. | One consistent picture. | The banner says "Cancel this document and create a new one" but Cancel is behind "..." ; the sub-label still says "Ready to post" above the red banner and Post is still offered. | Hide "Ready to post" while the banner is shown, name the menu in the sentence ("Use ... > Cancel document") or show Cancel in the banner. |
| NR-12 | Polish | Post result | Post a receipt. | A next step for someone with a pile of deliveries. | Posted document with "Stock updated." and no action; the worker uses the tab bar to start the next receipt. | Add "New receipt" (and "Back to list") on the Posted view for roles that can create. |
| NR-13 | Polish | Add item sheet | Type "bulk" (700 products). | Hint that more exist. | 8 rows and no "Showing 8 of 700, type more letters"; a miss gives "No product matches ..." with no next step (Staff cannot add products). | "Showing the first 8. Keep typing." and, on a miss, "Not found? Ask a supervisor to add it." |
| NR-14 | Polish | Receipts and Issues search box | Open Receipts at 360. | Placeholder fully readable. | "Number, partner or remark" shows as "Number, partner" (the Filters button takes the room). | Shorter placeholder ("Number or partner") or let the box use the whole row. |
| NR-15 | Polish | Touch targets | See section 5. | 44 px. | "IMS" brand link 61x24 on every screen; product-ledger document links 40 px high; Products list SKU links 40 px high. | Pad the links to 44 px or make the whole row the target. |
| NR-16 | Polish | Quantity stepper and line card | Look at a line. | Steppers look like buttons; the unit sits on the field's baseline. | The minus and plus are bare glyphs with no outline; "PCS" sits at the top right, above the field's middle. Each line card is about 230 px high, so five lines are about 1,150 px of scrolling. The footer total counts an out-of-range quantity such as 99999999999 (only 0 and negative lines are ignored). | Outline the steppers, align the unit, make the line card two rows (name and quantity on one row, Remove as an icon). |
| NR-17 | Polish | Product page (`/stock/2`) | Open a product that is below safety stock. | Same Low chip as the list. | "1 PCS" and "Safety stock: 100" with no Low chip (the list shows it). Each ledger entry is a card of about 280 px with five rows, so one screen shows one entry; the ledger shows the change only (no balance after, PS-14). | Show the chip, make ledger rows two lines (document and change; date and type). |
| NR-18 | Polish | Wording and announcer | ms and zh Home; save a new draft. | Same words for the same thing; the screen-reader announcer matches the page. | ms: Home button "Keluarkan stok" but tab "Keluar stok", and the Receive tab reads "Terima stok" like the Home button. The route announcer region keeps "New receipt - Receipts - IMS" after the document has been saved and the title changed (ER-16 family). | Native review of the three catalogs; refresh the announcer when the document title changes. |

## 7. What worked well (keep)

- The mistake-proofing of the first-day happy path is in place: one save is one document, Back in the Post dialog is safe, a repeat scan says "now 2", a shortfall is explained with a one-tap "Set to N".
- Add item: focused on open, the sheet leaves the form visible, Enter on a barcode or SKU adds and keeps the field ready, a miss is grey and calm, rows are 48 px.
- Lists are compact (about 120 px cards, one Filters button, Mine by default, the whole card is a target).
- Posted and Submitted views say what happened and what to do (Ready to post, Posted by, Awaiting approval with the reason, "Ask a supervisor to reverse").
- Staff see no cost or value anywhere; receipts work by quantity.
- zh and ms fit at 360 on every screen tried; language switch keeps the half-filled form.

## 8. Routing

- ui-ux-designer: NR-02 (decision: Edit on a Submitted document), NR-03, NR-04, NR-05, NR-11, NR-12, NR-13, NR-16, NR-17; also open items NV-08 (tappable Home tiles) and NV-10.
- fe-framework: NR-01 (first), NR-07, NR-08, NR-09, NR-10, NR-14, NR-15, NR-18; NR-06 is slice R02 of the refresh plan; NV-07 (restore), NV-25.
- be-framework: NR-02 (a return-to-draft command for the submitter of a Submitted document), if the designer chooses it.
- Re-test after the fixes: NR-01 on a real phone, NR-02 flow, NR-07 double tap, the footer at 360 (draft, submitted), the refresh slices R02 (login banner, required marker) and R10 (shell and action bar).
