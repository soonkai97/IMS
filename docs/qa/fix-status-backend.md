# Backend fix status for the three QA reports (2026-10-10)

Reports: `power-supervisor.md` (PS), `edge-accessibility.md` (EA), `novice-warehouse-staff.md` (NV). The reports themselves are not edited.

Delivered as four stacked pull requests (each later one is based on the earlier branch; merge in order):
**part 1** `feature/qa-backend-fixes-1` (who may do what with a document: review routing, ownership, reasons, refresh-token default),
**part 2** `-2` (estimated issue amounts and "why approval", the reversal-blocked message),
**part 3** `-3` (low stock, product import, audit search),
**part 4** `-4` (input limits, If-Match, lockout, rate-limit message, stale-document details, user rules, this list).

Legend: **fixed** = done and covered by a test; **not done** = no backend change made, reason given; **needs decision** = waits for a product answer; **UI** = no backend change is needed or possible, the frontend owns it (the contract it relies on is in `docs/team/HANDOFFS.md`).

## Power supervisor (PS)

| ID | Status | Notes |
|---|---|---|
| PS-01 | fixed (part 2) | A blocked reversal is `409` "Reversal blocked", `code: reversal_blocked`, the E6 sentence naming every product with its resulting balance and unit, plus `shortages[]`. The post-time conflict keeps `productId` and gets `code: insufficient_stock`; its sentence now names the SKU, warehouse and unit instead of ids (part 4). The frontend must read `code`. |
| PS-02 | fixed (part 3) | Product import rejects unknown categories and units as row errors (units match by code or name, case-insensitive); nothing is created. Bad numbers say "is not a number"; a duplicate SKU names the other row. |
| PS-03 | fixed (part 2) | An unposted issue is valued at the average cost when saved and again when submitted: `totalAmount` (document and list), line `unitCost`, `amountIsEstimate`; `approvalTriggers` says which rule threshold held it back. Posting books the real cost. |
| PS-04 | fixed (part 3) | Low-stock list, its total, the Home count and the export include active products with a safety level and no stock row, and flag negative balances (`isNegative`). |
| PS-05 | UI | Autofocus of the Add item search box. |
| PS-06 | fixed (part 3) | Audit: `entityType` is a partial case-insensitive match against the known entity names, new `entityId` (document number prefix), `action` prefix, `search` box, `actor` prefix, `GET /api/audit/filters` for dropdowns; two new indexes, no scans. |
| PS-07 | fixed (part 1) | `allowedActions` use the same policy as the commands (Owner-level documents, ownership, live approval rules); list items carry `canAct`. |
| PS-08 | fixed (backend part, part 4) / UI | The stale banner logic is UI. The stale 409 now has `code: document_changed`, `changedBy` (display name) and `changedAt` for the E4 sentence. |
| PS-09 | fixed (part 1) | Reject, reverse and override reasons: 5+ characters after trimming (FluentValidation and domain rule), stored trimmed. |
| PS-10 | fixed (part 1) | Reject by the submitter is `400` while self-approval is off. |
| PS-11 | UI / needs decision | The data is already in the DTO (`lines[].available` against `baseQty` on Submitted and Approved documents), so the warning is a frontend job. Allowing Cancel on an Approved document is the open UX question Q3. |
| PS-12 | fixed (part 3, in part) | Audit details write enum names (`"Status":"Submitted"`); sign-ins can be hidden with the `action` filter. Not done: one settings save still writes one row per changed key plus `settings.updated` (the per-key rows carry old and new values). |
| PS-13 | UI | Paging controls. The API already offers `afterId` seek paging and `pageSize`. |
| PS-14 | not done | Ledger "balance after" needs a stored running balance (or a window function over the ledger) on the append-only, trigger-protected table: a be-dba decision recorded earlier, not a safe change inside this task. |
| PS-15 | fixed (part 4) / UI / needs decision | `from` later than `to` is a `400` ("The 'from' date is after the 'to' date.") on documents, ledger, audit, movements and the two exports. Date format, empty states and the export message are UI. Showing a reversed document with a negative sign in the movements report is a product question (quantity is unsigned by design today). |
| PS-16 | fixed (parts 1 and 3) | "Posted today" excludes reversed documents; "Products with stock" counts positive balances only; definitions are in the DTO docs. Stock value stays net of negative lines (documented). |
| PS-17 | UI | Wording and layout; submitted-by/at are already in the DTO. |
| PS-18 | fixed (part 4, docs) | Rules fire at "from" (>=); `docs/design/mvp-ux.md` toast wording changed from "over" to "reaches". |
| PS-19 | fixed (part 3) | The template sample row uses fake barcodes; the unchanged template previews with 0 errors. |
| PS-20..24, PS-26 | UI | |
| PS-25 | fixed (part 4) | An `If-Match` without quotes is read like the quoted one, and a missing header is now `428` (see EA-19). |

## Edge cases, permissions, accessibility (EA)

| ID | Status | Notes |
|---|---|---|
| EA-01 | fixed (part 1) | Staff and Clerk edit, submit and cancel only their own drafts and recall only what they submitted (403 otherwise); Supervisor and Admin act on any. `allowedActions` match. |
| EA-02 | fixed (part 1) / needs decision (edge) | Owner approves and rejects only documents flagged Large; an override document that is not Large is a Supervisor/Admin act. Open: a document that is both Large and override is reviewed by the Owner or Admin (see `docs/design/mvp-ux.md` 7.2). |
| EA-03, EA-04 | UI | |
| EA-05 | fixed (part 1) | `Auth:ReturnRefreshTokenInBody` defaults to false in code and `appsettings.json`; `docs/security/refresh-cookie.md` updated; the switch stays. |
| EA-06..09 | UI | |
| EA-10, EA-11 | fixed (part 1) | Same as PS-09 and PS-10. |
| EA-12 | fixed (part 4) | `users.read` removed from Owner and Supervisor (the matrix says users and roles are Admin only; no screen used it). |
| EA-13 | fixed (part 4) | A correct password on a locked account answers `401` with the E7 sentence ("Too many failed attempts. Try again in N minutes."), `code: account_locked` and `retryAfterMinutes`; a wrong password or unknown user still gets the generic text (no account enumeration). The frontend must show `detail`. |
| EA-14, EA-15 | UI | |
| EA-16 | fixed (part 4) | `search` is capped at 100 characters on every list and export endpoint (`400`, not `500`). |
| EA-17 | fixed (part 4) | Unknown warehouse ids on create/update user are `400` "Warehouse does not exist." |
| EA-18 | fixed (part 4) / UI | Too-long product, partner, warehouse, category, unit and user names answer "... is too long (maximum N characters)." (the "is required" text is only for empty values). Aligning the form `maxlength` is UI. |
| EA-19 | fixed (part 4) | Every document command (`PUT`, submit, approve, reject, cancel, post, reverse) requires `If-Match`: `428` when missing; `*` stays accepted as the explicit "any version" opt-out. **The frontend must send the token on every document command.** |
| EA-20..22 | UI | |
| EA-23 | fixed (product decision 2026-10-10) | Warehouse staff see no cost or value anywhere; Clerk sees cost on documents but not stock value; Supervisor, Owner and Admin see everything. Permissions `cost.view` and `valuation.view`, applied in one pipeline step; see `docs/design/mvp-ux.md` 1.4 and `docs/architecture/backend.md` (Cost and value visibility). Frontend part: render nothing for a `null` cost or value and hide the columns and the Home value tile. |
| EA-24 | fixed (part 4) / not done in part | `429` answers a readable ProblemDetails ("Too many requests. Try again in N seconds.") with `Retry-After`, `code: rate_limited` and `retryAfterSeconds`. The login limiter stays keyed on the client address: keying on user plus address needs the request body read before authentication and was not done. |
| EA-25 | fixed (part 1) | A document in another warehouse is `404` for every document operation. |
| EA-26..29 | UI | EA-28 backend part: see PS-08. |
| EA-30 | fixed (part 4, in part) | An issue without a purpose is `400` (create and edit); user names are limited to letters, digits and `. _ - @ +`. Not done: refusing the same product on two lines. The availability check deliberately adds the lines of one product together, tests rely on it, and the UI merges duplicates; say if the API should refuse them. |

## Novice warehouse staff (NV)

All NV findings are screens, copy or navigation (UI) except:

| ID | Status | Notes |
|---|---|---|
| NV-06 | fixed (part 4) / UI | The post-time stock conflict names the SKU, warehouse and unit ("Not enough stock for DEMO-002 in Main warehouse: 0 PCS available, 24 needed."); what to do on a read-only document is UI. |
| NV-09 | UI | Field copy lives in the form; the API quantity message is unchanged. |
| NV-11, NV-13, NV-17 | UI | `safetyStock`, `isLow`, `isNegative`, `submittedByName`, `postedByName` are in the DTOs. |
| all others | UI | |
