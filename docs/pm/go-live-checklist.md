# Go-live checklist: turning the pilot on in a real environment

Owner: project-manager. Written 2026-10-22 at the close of PL2 (`main` at `3e99afa6`, PL1 and PL2 merged, F-1 to F-3 of the security retest fixed in #183). The pilot (accounting export, purchasing) is built and tested; it has not been switched on anywhere real. Work top to bottom; a step marked STOP blocks the next section. Detail lives in the files named in each row; this page does not repeat them.

## 1. Preconditions (nothing is switched on until these are answered)

| # | Condition | Who | Status | Where |
|---|---|---|---|---|
| 1 | STOP. The placeholder PO approval amounts (RM 5,000 Supervisor, RM 20,000 Owner) are reviewed and the real amounts are given | Owner | open | `pilot-slice-2-plan.md` section 11 question 2. Editable in Settings only once purchasing is on, so see 3.4 |
| 2 | STOP for accounting. One sample import file (or import template) of the accounting package is checked against the 17 export columns | Owner, accountant | open | `pilot-slice-1-plan.md` question 2; the layout is a file contract, a change is a new announced version |
| 3 | The accountant confirms a reversal row is dated by the REVERSAL (as the movements report does), not the posting date the plan text said, and that an export at cost is enough until PL3 brings selling prices | Owner, accountant | open | `TASKS.md` PL1.6 follow-up (b) |
| 4 | DBA records the accounting export at the 1M tier on `Ims_Perf` (reads, CPU, whether `IX_StockDocument_PostedAt` is sought) | be-dba | open (not a blocker for a small pilot: 50,000 rows measured at 1.1 to 1.9 s for the statement, 8 s end to end; record it before the first month-end export on a large database) | `docs/dba/README.md` section 11, "Still to do" |
| 5 | Hosting and backup decision (O3). Default: one small VM, Docker Compose, Caddy as the TLS proxy; nightly full plus 15-minute log backups; one restore drill before go-live | Owner | open, due Fri 23 Oct | `ROADMAP.md` 8.2; `docs/dba/README.md` section 8. `deploy.yml` is a skeleton: the deploy command for the chosen host is not written |
| 6 | F-1 to F-3 of the retest are fixed (#183 merged); CI is green on the `main` commit to deploy; the image comes from `release.yml` | Orchestrator | merged, confirm CI on the commit | `docs/reviews/pl2-security-retest/report.md` |
| 7 | zh and ms: accept the machine translations (`TODO-review`) or name native speakers (O10) | Owner | open | `ROADMAP.md` 8.2 |

## 2. Deployment

1. Back up the database and the file store (`Storage__RootPath`) and keep the backup until section 4 passes.
2. Configuration (environment variables; `docker-compose.yml` shows the shape, secrets from the shell or an untracked `.env`):
   - `ConnectionStrings__Default`: a SQL login that is not `sa` on a real host.
   - `Jwt__Key`: 32 or more characters, secret, identical on every instance. Document ETags are derived from it, so a rotation ends every session and gives each open editor one 409.
   - `Cors__Origins`: the exact web origin(s), https. Also used by the refresh-cookie CSRF check. Recommended: one host where the proxy serves the web build and forwards `/api/*` (`docs/security/web-headers.md`, apply its header set).
   - `Seed__AdminPassword`: used once, to create the first administrator.
   - `DataProtection__KeysPath`: a persistent, protected volume (compose volume `keys`). It encrypts list cursors; the key ring is stored unencrypted, so no one else may read the volume. Losing it breaks open page cursors only. A first `docker compose up` on the real host has never been tested: do it once before go-live.
   - `ForwardedHeaders__Enabled` and `ForwardedHeaders__KnownProxies` set to the proxy. Leave `Auth__ReturnRefreshTokenInBody` at its default (false); the web uses the cookie.
3. Migrations, the chain ends at `AddReceiptPurchaseOrderLinks` (20261021100000). Choose ONE way:
   - `Database__MigrateOnStartup=true` on a SINGLE instance (it also seeds; instances starting together race). Production default is off.
   - A DBA script: `dotnet ef migrations script <from> <to>`, run by hand with `sqlcmd -I -b`. `-I` sets QUOTED_IDENTIFIER ON for the filtered index, `-b` stops at the first error (without it a failed statement does not undo the others and the history row can still be written). Afterwards check `sys.indexes` for `IX_StockDocument_PurchaseOrderId`. The adds are metadata-only (58 ms at 1M documents, `docs/dba/README.md` section 12). A script-only deploy creates no administrator: start the API once with `MigrateOnStartup=true` and `Seed__AdminPassword` set (it writes only what is missing), then turn it off.
4. Start the new API and the web build. Users reload the page (the service worker serves the new bundle on the next load). Rights ride in a 5-minute token: an Administrator signs out and in, or waits 5 minutes, before the Modules screen offers the new switches.

## 3. Switch-on order (Administrator, Settings > Modules, a reason of 5 or more characters each time)

1. Accounting needs the build deployed and every client reloaded first (a bundle older than PL1.8a is harmless since the API follow-up #156, but a stale tab is not worth the risk). Then switch `accounting` on; the reply says how many roles regain `accounting.export` (Administrator and Owner by default).
2. Fill the tax fields where the accountant needs them (product HS and SST code, partner TIN and SST number). An export on a database with none is valid but thin.
3. Purchasing needs an ACTIVE purchase-order approval rule. The migration `AddPurchaseOrderApprovalRules` seeded the two placeholders, so the switch works; without an active rule it is refused (409).
4. Purchasing is live the moment it is on: Supervisor and Clerk already hold `po.manage`. So switch it on at a quiet time, open Settings > Approval rules at once, set the real amounts from precondition 1, and only then tell the buyers. Orders submitted before that are routed by the placeholders.
5. Check Settings: self-approval is off (default). Create the suppliers (partners of type Supplier) and the safety stock of the products the reorder list should watch. Give custom roles `po.read` / `po.manage` in the role editor (`po.manage` also needs `cost.view` and `stock.read`).

## 4. Smoke checks after switch-on (about 30 minutes; use a test warehouse or reverse and cancel afterwards)

1. Modules screen shows both on; the menu shows Purchase orders to Supervisor, Clerk, Warehouse staff and Owner (Owner reads only) and Reports > Accounting export to Administrator and Owner.
2. Clerk creates a PO of a few lines and submits it. Under the Standard amount it is Approved at once; above it, it waits and a Supervisor (not the Clerk) approves. A Supervisor cannot approve what they submitted.
3. Warehouse staff opens the PO, presses Receive, posts part of it: no price anywhere, the PO stays Approved, stock rose, the ledger shows the PO price as unit cost. Post the rest: the PO closes itself. A receipt above what remains is refused with the figures.
4. Reorder list for the warehouse shows a product below safety; creating a PO draft from it makes the row leave the list.
5. Accounting export for a short period downloads, opens, and its totals match the Movements report; the row carries the TIN and HS code. The audit log shows `module.enabled` with the reason, the PO events and `accounting.exported`.
6. Warehouse staff sees no price, amount or total on any purchasing screen. Clean up: cancel or close the test orders, reverse the test receipts.

## 5. Rollback

1. Accounting can be switched off at any time; data is kept and shows again when it is back on.
2. Purchasing is REFUSED (409 "cannot be turned off while N purchase orders are open") while any order is Draft, Submitted or Approved. Finish them: a Draft is cancelled by its creator or any `modules.manage` holder; a Submitted order is recalled and cancelled by its submitter, or rejected by a reviewer and then cancelled as a Draft; an Approved order with nothing received is cancelled by a reviewer of its tier with a reason; an Approved order with something received is closed short (`po.manage`, reason) after every linked receipt is posted or cancelled. Closed and Cancelled orders do not block the switch.
3. Do not roll the schema back after real use: `Down` of `AddPurchaseOrders` and `AddReceiptPurchaseOrderLinks` refuse while any order or link exists. To return to a build older than PL2, restore the backup of section 2 step 1.

## 6. Known limitations (accepted for the pilot)

- No offline use: commands are never queued (rule N9) and the service worker keeps no API data.
- ms and zh are machine translations, marked `TODO-review`.
- Editor-then-approver: a Supervisor can edit a Draft, have a Clerk submit it, then approve it (owner question 6; audit shows both). Rights changes reach a signed-in person within about 5.5 minutes.
- Print: the browser adds its own header and footer (address, order id) to the PO printout; switch them off in the print dialog. A printout needs `cost.view`.
- F-4 accepted by the owner: login timing tells an existing username from an unknown one by 15 to 50 ms (rate limit and lockout bound it; `docs/security/2026-10-pl2-retest-follow-ups.md`). Revisit if the API faces the public internet without them.
- One warehouse per PO; no tax, discount, currency or delivery date on an order; reorder is 2 x safety less on-order, with no lead time or supplier; over-receipt has no tolerance; an export is capped at 50,000 rows.

## 7. Owner decisions still open

1. PO approval amounts (section 1, row 1). 2. Accounting package sample file and the accountant's answers (rows 2 and 3). 3. Hosting and backups, O3 (row 5). 4. zh and ms reviewers, O10 (row 7). 5. Smaller, defaults hold unless you object: reorder counts Draft, Submitted and Approved orders as on order; the PO printout needs `cost.view`; segregation of duties (question 6); required e2e smoke check (O11); cleanup of old approval-rule duplicates in `Ims_Dev` (development database only).
