# PL2.11 security retest: purchasing pilot

Reviewer: security-engineer. Date: 2026-10-22. Code under test: main at 10cb84d (PL2.9 merged); main has since moved to 086d4415 (#180), not covered. Probes: `tests/security/pl2-purchasing/` (manual, not CI; README there).
Scope: purchase orders (list, get, create, update, submit, recall, approve, reject, cancel, close short), receipt from an order, posting against an order, reorder suggestions, the accounting export, and the module and role administration that gate them. Nothing in `src/`, `web/` or `tests/Ims.*` was changed.

## 1. Executive summary

No Critical, High or Medium finding. Five Low findings (F-1 to F-5) and Info observations (section 5). The authorization matrix (150 cells), segregation rules, cost hiding, warehouse scope and the races held up: every cell answered as the plan and permission catalog say, no price reached a caller without cost.view, no order of another warehouse was readable or changeable, and two receipts racing for the last quantity gave exactly one winner in all 8 rounds.

| Area | Result | Checks (ok / FAIL) | Findings |
|---|---|---|---|
| 1 Authorization matrix | PASS | 150 / 0 | none |
| 2 Segregation and approval tiers | PASS | 49 / 0 | known gap (owner question 6) shown as-is |
| 3 Cost hiding | PASS | 44 / 0 | Info I-2 |
| 4 Warehouse scope and IDOR | PASS | 41 / 0 | none |
| 5 Mass assignment and input | FAIL (Low) | 81 / 2 | F-1, F-2 |
| 6 Concurrency and integrity | FAIL (Low) | 37 / 1 | F-2 |
| 7 Information disclosure and sessions | FAIL (Low) | 47 / 4 | F-1, F-3, F-4 |
| 8 Module and role administration | PASS | 52 / 0 | none |
| 9 Config, dependencies, logs, clean-up | FAIL (Low) | 21 / 0 (+ manual) | F-5 |

(Counts = last run of every script, results de-duplicated by label.)

| ID | Sev | OWASP API 2023 | Title | Minimal fix |
|---|---|---|---|---|
| F-1 | Low | API8 | Malformed JSON types echo CLR type names in the 400 (also anonymous login) | `ProblemResponses.cs:33` |
| F-2 | Low | API1/API5 | `If-Match: *` switches off the required concurrency token | `IfMatchHeader.cs:18` |
| F-3 | Low | API8, ASVS 8.2 | No Cache-Control on authenticated JSON that carries cost data | `SecurityHeadersMiddleware.cs:34` |
| F-4 | Low | API2 | Login timing tells an existing username from an unknown one (+15 to +50 ms) | `LoginHandler.cs` |
| F-5 | Low | API8 / supply chain | Three open Dependabot alerts: vite 8.3.0 (dev server) via launch-editor | bump vite to 8.3.3+ |

## 2. Method

Release publish of the API from the worktree on loopback 5960 (Development, `Cors:Origins` a local origin) and, for production-only checks, 5961 (Production, same database). Database: the ONE dev database Ims_Dev; no scratch database. `purchasing` and `accounting` switched ON via `PUT /api/modules/{code}` with a reason (active PO approval rules: RM 5,000 Standard, RM 20,000 Large; self-approval OFF). Throw-away users, roles, supplier, customer and products created through the API with prefix `SEC-`. Forged and expired tokens minted with the Jwt:Key the throw-away process was started with. Probes are Node scripts using only fetch; passwords from the environment, none in this report.

## 3. Findings

### F-1 (Low, API8) Malformed JSON types echo CLR type names in the 400

CVSS-style AV:N/AC:L/PR:N/C:L, about 3.7. Every JSON endpoint, including anonymous `POST /api/auth/login`; reproduced in Development and Production.

```
POST /api/purchase-orders {"warehouseId":"…","supplierId":"…","lines":{}}
400 {"title":"Request rejected","status":400,"detail":"The request is not valid.","errors":{"body":["The body field is required."],"lines":["The JSON value could not be converted to System.Collections.Generic.IReadOnlyList`1[Ims.Application.Features.Purchasing.PurchaseOrderLineInput]. Path: $.lines | LineNumber: 0 | BytePositionInLine: 115."]}}

POST /api/auth/login (no token) {"username":{},"password":1}
400 … "username":["The JSON value could not be converted to Ims.Application.Features.Auth.LoginRequest. Path: $.username | LineNumber: 0 | BytePositionInLine: 13."]
```

Impact: assembly layout and type names to anyone. The comment above `ProblemResponses.FromModelState` says framework messages are replaced; this only happens when ErrorMessage is empty.
Fix (be-framework): `src/Ims.Api/Common/ProblemResponses.cs:33`: `string.IsNullOrWhiteSpace(e.ErrorMessage) || e.Exception is not null ? Messages.RequestNotValid : e.ErrorMessage`. Add an API test posting `{"lines":{}}` asserting no "Ims." in the body.

### F-2 (Low, API1/API5) `If-Match: *` disables the required concurrency token

AV:N/AC:L/PR:L/I:L, about 3.1. Needs an API client that deliberately sends `*`; the web always sends the token.

```
POST /api/purchase-orders/{id}/approve  If-Match: *  (Submitted order, token not read first) -> 200 {…"status":"Approved"…}
POST …/approve (no If-Match) -> 428;  If-Match: "AAAAAAAAAAAA" -> 409 {"code":"document_changed"}
```

`IfMatchHeader.Read(request, required: true)` returns null for `*` (`src/Ims.Api/Common/IfMatchHeader.cs:18`), read by handlers as "no check". Applies to all PO transitions and to document commands (approve, post, reverse). Impact: approve/post/cancel of content that changed since it was read; widens the editor-then-approver gap.
Fix: when required, treat `*` as missing (428); keep for `required:false` (roles). `tests/smoke/smoke.mjs` sends `If-Match: *` on document commands and must send the real token.

### F-3 (Low, API8, ASVS 8.2) No Cache-Control on authenticated JSON with cost data

`SecurityHeadersMiddleware.cs:34` adds no-store only under `/api/auth`; exports and attachments set their own.

```
GET /api/purchase-orders/{id} -> 200, nosniff, X-Frame-Options: DENY, Referrer-Policy: no-referrer, CSP default-src 'none' (no Cache-Control)
GET /api/accounting/export?from=…&to=… -> Cache-Control: private, no-store (held up)
```

Impact: a browser or proxy may keep cost data after sign-out on a shared PC. Fix: `headers.TryAdd(HeaderNames.CacheControl, "private, no-store")` for every `/api` path (TryAdd keeps the exports' and attachments' own).

### F-4 (Low, API2) Login timing separates an existing username from an unknown one

AV:N/AC:H/PR:N/C:L, about 3.1 (mitigated by 20 logins/min per address and the 5-failure lock). Same status and message for both (held up); the dummy hash equalises hashing cost, but an existing account also writes (failed-login count, audit row). Median of 8 interleaved samples after warm-up, three quiet runs: existing 87/159/106 ms vs unknown 74/75/58 ms (`p7b-timing.mjs`; load widens the gap).
Fix (optional): in `LoginHandler.TryAsync` do the same write for an unknown user, or pad every failed login to a fixed minimum (e.g. 250 ms). Accept as risk if the owner prefers.

### F-5 (Low, supply chain) Open Dependabot alerts: vite 8.3.0 (development scope)

#33 (medium, GHSA-698v-4r3x-3vcv), #34 (high, GHSA-587h-7wmj-h5pm), #35 (medium, GHSA-r825-w8mm-5pxw): launch-editor code bundled in vite 8.3.0 to 8.3.2, fixed in 8.3.3, scope development (dev server on Windows). `npm audit --omit=dev`: 0. `npm audit` (all): 37 (1 low, 25 moderate, 11 high, 0 critical), all dev tooling (stylelint, jest/nx chains, vite). CodeQL: 0 open. `dotnet list package --vulnerable --include-transitive`: none in all 7 projects. Fix (fe-framework): raise vite to 8.3.3+ in `web/package-lock.json`.

## 4. Per-area evidence (what held up)

1. **Authz (150 cells).** Callers: Administrator, Owner, Supervisor, Clerk, Warehouse staff, po.read-only role, document.post-without-po.read role, no-rights role, anonymous, validly signed role=Admin token without perm claims, expired token. Endpoints: list, get, create, update, submit, recall, approve, reject, cancel, close, receipt, suggestions, accounting export (print view = same GET). Denied cells 403 (anonymous and expired 401); the three shared orders unchanged afterwards. Open doors 200 on fresh orders in the right state. document.post without po.read: `POST …/receipt` 403. Tampered payload, alg none, wrong key, audience or issuer, garbage: 401. Forged role=Admin without perms: 403 everywhere (decisions by perm, not role).
2. **Segregation.** Self-approve and self-reject refused for Administrator, Supervisor and an Owner-like custom role (HTTP 400 "You cannot approve a purchase order you submitted.", same as documents); another holder then approves (200). Owner on Standard 403; Supervisor on Large 403 "This document is above your approval limit."; Administrator either 200. Boundaries: 4,999.99 released at submit; 5,000.00 and 19,999.99 Standard; 20,000.00 Large. Submitted Large cannot be edited down in place (409); Approved not editable (409). Close short with nothing received: 409 "Nothing has been received against purchase order …, so it cannot be closed short. Cancel it instead." (Clerk and Administrator). Clerk cannot cancel Approved (403); Owner cannot cancel Standard-tier Approved; Supervisor cannot cancel Large-tier; another Clerk cannot cancel or recall (403). Administrator cancels any Draft (designed). KNOWN GAP shown as-is (owner question 6): a Supervisor who created and edited a Draft approved it after a Clerk submitted it (200/200).
3. **Cost hiding.** For Warehouse staff, a po.read-only role, an audit-only role and an approver without cost.view: totalAmount, unitPrice, amount are JSON null on get and list; marker price 731.29 / amount 2193.87 occur in no raw body of order, list, suggestions, receipt draft, document list/get, ledger, stock, audit log, approval triggers, approval-rule thresholds. The over_receipt 409 for staff carries quantities only ("Purchase order line 1 (SEC-…-A): 6 PCS is more than remains. Remaining 2 PCS (ordered 6, received 4)." code over_receipt, lines […]); a poster without po.read gets the general 409 "This receipt asks for more than the purchase order has left to receive." with no order number or figures. Staff cannot create an order (403). A unit cost typed by staff is accepted and discarded (200; stored cost stays the order price, or null on a plain receipt), not 403 (I-2). A staff-made receipt is valued at the order price (ledger unit cost 731.29).
4. **Warehouse scope.** Users limited to A (Clerk, Supervisor, Staff, custom) got 404 on get, update, submit, recall, approve, reject, cancel, close and receipt of warehouse-B orders, body identical to a random GUID (except correlation ids); lists, warehouseId=B and number search return nothing; B receipts invisible by id and in lists; `suggestions?warehouseId=B` is 404 like an unknown warehouse; create for B and for an unknown warehouse give the same 400; 40 random ids all 404; median 4.9 ms forbidden vs 5.0 ms unknown. Reverse direction and foreign ids in bodies refused 400/404. Scoped export user: 404 for warehouse B, only A rows otherwise.
5. **Mass assignment and input.** Twenty extra properties on create ignored; purchaseOrderId in an ordinary receipt body ignored; purchaseOrderLineNo on an unlinked receipt 400; linked draft naming a missing line or another product 400. Zero, negative, over-precise and overflowing quantities and prices, 501 lines, 100 kB remarks, lines [null]/null/{}, NaN, duplicate keys, a 20,000-deep array and an 8 MB body: all 4xx (F-1 for the type-name text); 500 lines save and read back in 434 ms. NUL, bidi override, CRLF plus header text, markup, emoji and SQL quotes in remarks stored inert or refused, never reach a header. Searches "'; DROP TABLE…", "' OR 1=1--", %%%, ___, [a-z], backslashes and NUL match nothing and break nothing; invalid enum, paging and cursor values 400 or clamped (page size cap 200). An X-Correlation-Id of 200 characters or markup is replaced by a generated id. Accounting export of names starting `=HYPERLINK(…)`, `@SUM(1+1)`, `+1+1`: shared-string cells, no formula element in the file (I-5).
6. **Concurrency (8 rounds each).** Two receipts posting the last quantity at once: exactly one 200 and one 409 each round, order Closed/Received, stock grew by exactly 10 per round. Cancel vs receipt creation: exactly one wins. Cancel vs post of a submitted linked receipt: cancel never wins. Approve vs recall, approve vs reject, three approvers at once: exactly one 200, stored status matches. Four simultaneous submits with one token: 200, 409, 409, 409; replayed approve 409; stale token 409 document_changed; missing If-Match 428; 20 parallel creates: 20 distinct gap-free numbers. Partial receipt (6 of 10 stays Approved), draft above remainder 400, zero or negative 400, reversal frees quantity, close short needs 5 characters and no waiting receipt, full receipt closes (Received), reversal does not reopen, no receipt from Closed or Cancelled (409). Changing or removing a unit conversion on an order: 400 naming the unit, not the order. Only `If-Match: *` failed (F-2).
7. **Disclosure and sessions.** 4xx/5xx bodies of 10 cases in Production mode: no stack, SQL, path or type name except F-1. Headers: nosniff, X-Frame-Options DENY, CSP default-src 'none', Referrer-Policy no-referrer, no Server/X-Powered-By version; Swagger and `/openapi/v1.json` not served in Production (401); HSTS wired (`PipelineSetup.cs:20`) but not testable over loopback HTTP. CORS: hostile, null, look-alike, other-port, other-case and other-scheme origins get no Access-Control-Allow-Origin on GET or preflight; the configured origin is echoed exactly with credentials. Inventory: 84 documented operations, all but login/refresh/logout/health answer 401 without a token; PATCH/DELETE on an order 404/405. Access token 300 s; refresh cookie HttpOnly, SameSite=Strict, Path=/api/auth, not in the body; cookie refresh from a hostile Origin refused; rotated refresh token replayed 401; logout kills it. Lockout after 5 failures (right password gets the locked sentence, wrong one only the generic message); wrong-password and unknown-user messages identical (timing: F-4). Change-password: 401 anonymous, wrong current 400, short or unchanged 400, failed guesses count toward the lock. Rate limits: the 21st login in a minute 429 with Retry-After; 660 quick requests as one user gave 60 x 429, no 5xx. Logs (19.5 MB, 5 files, request lines of every probe): no seed or probe password, signing key, marker password, TIN, SST number or marker price; no JWT, Authorization or Set-Cookie value, no refresh token, no stack for a 4xx.
8. **Modules and roles.** `PUT /api/modules/{code}`: 403 for Owner, Supervisor, Clerk, Staff, po.read-only, no-rights; 401 anonymous; 403 for forged role=Admin; list readable by any signed-in user; no reason or no "enabled" 400. Switch OFF with 119 open orders: 409 "The Purchasing module cannot be turned off while 119 purchase orders are open. Finish or cancel them first.", nothing changed, no order numbers. Role closure: po.manage without cost.view, stock.read or po.read and po.read without masterdata.read are 400 naming the code, also when removing it from an existing role; a role manager lacking modules.manage cannot grant it (403 "You can only grant permissions you hold yourself."). Module OFF: all seven purchasing and accounting endpoints 404 for all seven signed-in caller kinds and for a token without perms; anonymous 401; `/api/permissions` omits po.read, po.manage, accounting.export; role save naming po.read 400 "not available while its module is off"; a role holding po.read keeps it when saved with the visible set and regains it on enable (rolesRegainingGrants 26); a PO approval rule cannot be saved while off, the list hides them. While on, the last active PO rule cannot be deactivated (200, 409). Enabling with no active rule: 409 "Purchasing cannot be turned on yet: no approval rule for purchase orders is active…", stays OFF (reachable only in the data, so set by SQL and the two found rules put back, verified column by column).
9. **Config, supply chain, clean-up.** Tracked-file secrets scan (`p9-config.sh`, 2,429 files): no private key, vendor key, JWT or connection-string password; eight assignment hits are made-up test fixtures. Workflows: all set `permissions:` (read-only except security-events: write in CodeQL and packages: write in Release), no pull_request_target, untrusted values via `env:`; CI and DAST passwords labelled CI-only service-container values. Dockerfile: non-root `USER $APP_UID`, no secrets in layers; compose: read_only, cap_drop ALL, no-new-privileges, ports on 127.0.0.1, secrets required from env. Dependencies: F-5.

## 5. Info observations

- I-1 Self-approval is refused with HTTP 400, not 403 (same as documents).
- I-2 A price typed by a caller without cost.view is discarded with 200 instead of 403 (`DocumentLineBuilder.cs:15`); nothing is stored.
- I-3 `POST …/receipt` for a caller without po.read answers 403 with the reused sentence "Reviewing or cancelling a purchase order needs the permission to read purchase orders." (wording only).
- I-4 Refresh tokens rotate and a replay is 401, but there is no reuse detection: after a replay the newer token still works.
- I-5 Text starting with `=`, `+`, `-` or `@` is written verbatim into the .xlsx as a string cell (inert in Excel); a CSV re-save would make it a formula. Optional: prefix with `'` in the writer.
- I-6 The data-protection key ring (list cursors) is persisted unencrypted (start-up warning NoXMLEncryptorConfigured); the keys directory must be a protected volume.
- I-7 Images and actions are referenced by tag, not digest (only `dast.yml` pins by SHA); compose connects the API to SQL Server as sa. Documented earlier in `docs/security`.
- I-8 modules.manage can be delegated by a role manager who holds it; the default holder is the Administrator only.
- I-9 Creating an order has no idempotency key (two POSTs, two orders); by design.

## 6. State left behind

Restored: Module.purchasing and Module.accounting OFF (as found, checked by query); no PO Draft, Submitted or Approved; every receipt of the probes Reversed or Cancelled (Draft, Submitted and Posted document counts equal the counts found before the run); stock of SEC- products 0 (ledger and audit rows remain as history); all SEC- users, roles, partners and products inactive (users moved to the least-privileged system role first, because a role cannot be switched off while any user, even inactive, holds it); approval rules 10 and 11 active with original REC_REVDATE and REC_REVISOR (NULL); API processes stopped.
Residue in Ims_Dev that cannot be undone through the API: closed and cancelled orders, reversed receipts, ledger and audit rows with SEC data, inactive SEC- rows.

## 7. Retest

Follow `tests/security/pl2-purchasing/README.md` (steps 0 to 14, about 40 minutes with the login-window waits). After the fixes the matching lines turn ok: F-1 "lines {}" in p5 and the F-1 line in p7; F-2 "If-Match *" in p5 and p6; F-3 the two Cache-Control lines in p7; F-4 `p7b-timing.mjs`. F-5 is cleared by `bash p9-config.sh deps`.
