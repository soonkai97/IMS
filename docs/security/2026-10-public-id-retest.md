# Security retest: the public-id (GUID) switch (PR-10, security part)

Role: security-engineer. Date: 2026-10-13. Mode: dynamic test of our own code on localhost.
Target: `origin/feature/public-ids-integration` at `2f266ee` (PR #78 to `main`, not merged). Standards: OWASP API Security Top 10 (2023) API1, API3, API5, API8; ASVS 4.0.3 V4, V8, V13.
Requirement under test: `docs/architecture/adr-guid-keys.md`, owner decision **D2** ("ids must not be guessable or countable in URLs and responses"), D1 (no client-minted ids), section 6.1/6.2 tests, PR-10 row "IDOR and client-id abuse retest". Scripts: `tests/security/public-id-retest/`.

## 0. Executive summary

**Verdict: not safe to merge #78 as it stands, but only because of two small, well-bounded Major findings. There is no Blocker: no IDOR, no existence oracle, no cross-warehouse read or write, no client-chosen id and no auth bypass was found.**

| Severity | Count | IDs |
|---|---|---|
| Blocker | 0 | |
| Major | 2 | PID-01, PID-02 |
| Minor | 3 | PID-03, PID-04, PID-05 |
| Info | 4 | PID-06 to PID-09 |

The switch itself holds: 20 id-taking routes x 10 actors x 9 to 12 id variants (about 2,000 requests) gave byte-identical answers for "does not exist", "wrong kind", "soft-deleted" and "out of scope" for every scoped caller; ids in query strings, bodies and upload forms behave the same; no endpoint accepts a client-chosen id; cursors are authenticated-encrypted, list-bound and cannot widen a scope.

What is not closed is **D2 itself**: two database-wide counters still leave the API and a warehouse-scoped user can use them to measure what other warehouses do.
- PID-01: `LedgerRow.id` (the ledger's bigint identity) is returned to every role that can read stock, including WarehouseStaff and Clerk.
- PID-02: the document `concurrencyToken` / `ETag` is the raw SQL `rowversion`, one counter for the whole database.

Both are an information leak about volume only (no content), so CVSS-style they are Medium (about 4.3, AV:N/AC:L/PR:L/UI:N/C:L). They are Major here because the accepted ADR says the hardening ("no id on ledger and audit rows") is part of option 1, the web has no real use for either value, and each fix is about an hour. Either fix them, or the owner records in writing that D2 is satisfied for GUID-bearing ids only.

## 1. Scope and method

- Built the integration branch in Release (`dotnet publish -c Release`) from a clean worktree and ran it from the publish folder against a new database `Ims_PidRetest10` on `(localdb)\ImsDev` (never the default instance; none of the protected databases was touched; the database was dropped at the end, the four API processes I started were stopped by recorded PID). Development mode with the seeded demo users (`src/Ims.Api/appsettings.Development.json`) for the matrix; Production mode with a key-ring folder for the operations checks.
- Actors (real sign-ins): Admin, Owner, Supervisor (WH01), Supervisor2 (WH02), WarehouseStaff (WH01), Staff2 (WH02), Clerk (WH01), Clerk2 (WH02), `noscope` (WarehouseStaff with no warehouse, "no permission to see anything"), and anonymous.
- Data: 3 warehouses, 5 users plus 5 created for the test, 2 posted, 3 draft and 1 submitted documents, 4 attachments, and one soft-deleted row of every kind (user, unit, category, partner, product, approval rule, document, attachment, warehouse), set by SQL on my own database.
- Id variants per route: unknown GUID, empty GUID, wrong-kind GUID (a product id on a document route and so on), soft-deleted, out-of-scope real id, in-scope real id, integer `1`, `-1`, a 23-digit number, `abc`, a SQL fragment. Compared: status, normalised body, all headers except date/correlation id, and median timing (80 interleaved requests per case).
- Code read (no edits): `CursorToken.cs`, `DataProtectionCursorProtector.cs`, `CursorProtectionSetup.cs`, `DocumentVersion.cs`, `Program.cs`, `Dockerfile`, `docker-compose.yml`, `web/` route, storage and service-worker code.

Not done: no 1M-tier run (the DBA part of PR-10); no load beyond a few hundred requests; no fuzzing of file contents (covered by `upload-scanning.md`); PR-7 minor items M1 to M4 (policy order for Owner uploads, audit payload wording) were not retested.

## 2. Findings

Ranked. "Evidence" is the exact request and what came back; the scripts reproduce it.

| ID | Sev | OWASP | Title |
|---|---|---|---|
| PID-01 | Major (CVSS 4.3) | API3 / API8, ASVS 8.3 | Ledger row id is a global counter returned to every role |
| PID-02 | Major (CVSS 4.3) | API3 / API8 | Document `concurrencyToken` and `ETag` are the database-wide `rowversion` |
| PID-03 | Minor | API8, ASVS 6.4 | Cursor key ring is stored unencrypted on the volume |
| PID-04 | Minor | API3 | Audit `id` and audit before/after payloads still carry integer keys |
| PID-05 | Minor | API8 | Sub-millisecond timing difference on attachment lookups |
| PID-06 to PID-09 | Info | | see section 4 |

### PID-01 Major: `LedgerRow.id` is the global bigint identity, visible to every role

- Where: `src/Ims.Application/Features/Stock/Contracts/LedgerRow.cs:6` (`long Id`); `GET /api/stock/ledger` is under `Permissions.StockRead` (`StockController.cs:15`), so WarehouseStaff, Clerk, Supervisor, Owner and Admin all get it (scope-filtered rows, unfiltered ids). The brief assumed the ledger is Owner/Admin only; that is true for `GET /api/audit` (403 for Staff), not for the ledger.
- Why it matters: ids are shared by all warehouses. A scoped user sees gaps in the sequence; the gap is the number of ledger rows written elsewhere. This is exactly the leak ADR section 1 ("A real leak that sequential global ids cause") gave as the case for D2, and the ADR recommendation (section "Recommendation") says to drop `id` from `LedgerRow` and `AuditLogDto`. PR-7 kept both as allow-listed exceptions in `OpenApiDocumentTests`.
- Evidence (`node counters.mjs`, Staff2 is scoped to WH02 only):
  - `GET /api/stock/ledger?pageSize=5` as Staff2 returns `items[].id` such as `6`, then, after the Admin posted 4 documents in WH01 and Staff2 posted one of its own: `11`. Delta 5 = 4 foreign rows + 1 own. Every row Staff2 sees has `warehouseCode: "WH02"`; the foreign rows are never shown, only counted.
  - `GET /api/stock/ledger?pageSize=1` as WarehouseStaff -> `200`, first row keys `id,occurredAt,sku,productName,warehouseCode,...`.
- Impact: an authenticated low-privilege user can measure posting volume of other warehouses over time (rate, busy hours). No content, cost or product is exposed.
- Not exploitable via cursors: the cursor is encrypted (section 3), so only the `id` field leaks.
- Fix (minimal): remove `Id` from `LedgerRow` (and from `AuditLogDto`, see PID-04); the web never reads `LedgerRow.id` (the table tracks rows by index) and reads `AuditLogDto.id` only inside one aria-label (`audit-log.component.html:46`, replace it with the time and action already in the label); the seek position already travels in `nextCursor`. Update the allow-list in `OpenApiDocumentTests` to empty. About 1 hour including tests.

### PID-02 Major: `concurrencyToken` / `ETag` leak the database-wide `rowversion`

- Where: `DocumentVersion.cs:10` (`Token = Base64(RowVersion)`), `DocumentDto.cs:42`, `DocumentListItem.cs:22`, `DocumentsController.cs:89` (ETag). `RowVersion` is a SQL Server `rowversion`, one 8-byte counter for the entire database, incremented by every insert or update of any table (documents, ledger, audit, balances, users, refresh tokens).
- Evidence (`node counters.mjs`): Staff2 creates a draft in WH02 and decodes the token as a big-endian 64-bit number: `2347`. The Admin posts 3 documents in WH01. Staff2 creates another draft: `2384`. Delta 37 = the number of rows written in the database in between (ledger rows, balances, document revisions, audit rows, sign-ins). `GET /api/documents/{id}` as WarehouseStaff returns `ETag: "AAAAAAAACHw="` (= 2172) and the same value as `concurrencyToken`; the list items carry it too.
- Impact: same class as PID-01 but covers all writes, including logins; works for any user who can read or create a document. It also lets a caller see how fast the system is used.
- Fix (minimal, keeps If-Match semantics): derive the token instead of exposing it: `Token(rowVersion, publicId) = base64url(HMACSHA256(serverKey, publicId || rowVersion))` truncated to 16 bytes, and make `EnsureCurrent` recompute it and compare with `CryptographicOperations.FixedTimeEquals`. The server key can be derived from the Data Protection key ring or a configured secret. Only `DocumentVersion` and its two callers change; the web treats the token as opaque (`web/libs/core/http/.../if-match.ts`). About 1 to 2 hours with a test that two edits give different tokens and that the token contains no monotonic part.

### PID-03 Minor: the cursor key ring is plain XML on the volume

- Evidence: the key file written to `DataProtection:KeysPath` contains `<!-- Warning: the key below is in an unencrypted form. -->` and the master key in the clear (ASP.NET Data Protection default when no `ProtectKeysWith...` is configured). Docker image: `DataProtection__KeysPath=/app/keys`, named volume `keys` owned by `$APP_UID` (`Dockerfile:22-24`, `docker-compose.yml:39`).
- Impact: anyone who can read the volume can forge cursors. A forged cursor only moves the start of a page inside the filter and scope the server still applies (section 3, item 5), so impact is low; the same volume holder could also read the database password from the container environment.
- Fix: document that the `keys` volume must be readable only by the app user (mode 0700) and excluded from shared backups; no certificate protection is needed for this purpose. Keys roll every 90 days and old keys stay in the ring, so outstanding cursors keep working (accepted, a cursor carries no authority).

### PID-04 Minor: audit rows still carry integer keys

- `AuditLogDto.id` is the audit bigint identity (`GET /api/audit?pageSize=3` as Admin: `"id":127`). `audit.read` is Owner/Admin only (Staff gets 403), and both see every warehouse, so there is no cross-scope leak. Before/after JSON payloads contain integer `Id`, `WarehouseId`, `ProductId` (sample keys `Type,WarehouseId,Lines`); 2 of 200 audit rows had an integer `entityId` (`ProductBarcode`). Accepted in the PR-4 and PR-6 reviews.
- Fix: drop `AuditLogDto.id` together with PID-01 (the cursor replaces it). Leave payloads as they are; note in the runbook that audit history is split between integer and GUID ids.

### PID-05 Minor: small timing difference on attachments

- 80 interleaved requests as Staff (WH01): `GET /api/attachments/{id}`: unknown median 3.3 ms, soft-deleted 3.2 ms, wrong kind 3.3 ms, **out-of-scope 4.0 ms** (p90 5.3 vs 4.5). Documents: 6.7 / 7.5 / 7.2 / 7.0 ms (unknown / soft-deleted / wrong kind / out-of-scope), lists 3.5 to 3.6 ms for all. A 0.7 ms gap is below network jitter on any real link and the caller would need a foreign attachment GUID (random v4) to test; I do not consider it exploitable. Recorded so nobody is surprised later. No fix needed.

## 3. What held up (evidence)

1. **IDOR matrix on every id route** (`idor.mjs`): `PUT warehouses/partners/units/categories/users/products/approval-rules/{id}`, `GET/PUT products/{id}`, `GET/PUT documents/{id}`, `submit/approve/reject/cancel/post/reverse`, `GET/DELETE attachments/{id}`, `GET attachments?ownerType&ownerId` (both owner types). For each scoped actor the unknown, empty, wrong-kind, soft-deleted and out-of-scope variants form **one** response group (status, body, headers identical). The 15 combinations flagged by the script are Admin/Owner, for whom the "out-of-scope" document is in scope; they are the control. Typical answers: permission first (403 identical for every id when the role lacks the permission), then 404 `Document not found.` for anything the caller cannot see, then 409 for a real id with a stale `If-Match`.
   - Staff, WH01: `GET /api/documents/<unknown>`, `<WH02 posted>` and `<soft-deleted>` all return `404`, `application/problem+json`, body `{"detail":"Document not found.","status":404,"title":"Not found","type":".../rfc9110#section-15.5.5"}` and identical headers.
   - `noscope` (no warehouse) gets 404 even for a real document; anonymous gets 401 on every id and shape.
2. **Ids outside the path** (`idor-fields.mjs`, about 170 cases per run): `warehouseId`, `productId`, `categoryId`, `sourceDocId` filters on documents, stock, ledger, low-stock, movements and the three exports: every well-formed variant gives the same `200` with `items=0 total=0` (or identical exports) for scoped users; malformed values give the same `400`. Create bodies: `warehouseId` (unknown, wrong-kind, soft-deleted and **out-of-scope all give the same 400 "Warehouse does not exist."**; PR-4 minor M1 is closed), `partnerId`, `lines[].productId`, `lines[].uomId`, product `categoryId`/`baseUomId`, user `warehouseIds`: identical per field. Upload `ownerId`: integer, negative, huge and missing give 400; unknown / wrong-kind / soft-deleted / out-of-scope give the same 404 per owner type; Product uploads by a role without `masterdata.manage` give 403 for every id. PR-6 minor M2 (integer `ownerId`) is closed.
3. **D1 and mass assignment** (`client-ids.mjs`, 20 checks, all pass): POST bodies carrying `id`, `Id`, `publicId`, `PublicId`, `key`, `createdById`, `createdBy`, `status`, `no`, `recDeleted`, `rowVersion`, `role`, line `id` and `documentId`, plus `?id=` and `?publicId=` in the query, `X-Request-Id` and `Idempotency-Key` headers and `id`/`publicId` form fields on upload: the entity always gets a server-made id, status stays `Draft`, the number is server-made, `createdById` is the caller, another document's lines are untouched, a PUT does not change id, `createdById`, status or number, two identical creates make two documents. The supplied id never resolves (`GET` gives 404). No endpoint accepts a client-chosen id.
4. **Ids are random**: `PublicId` values are version 4; the column default is `newid()`; all nine `UX_*_PublicId` indexes are unique and **unfiltered** (a soft-deleted id can never be reused). Consecutive documents have unrelated ids.
5. **Cursors** (`cursors.mjs`, every check passes): 134-character base64url, constant length, two cursors for one position differ (random IV). Flip first/middle/last character, truncate, append, space, `=` padding, standard-alphabet spelling, `%00`, the old integer form, a negative number, a GUID, SQL text, 4,000 characters, 100,000 characters (414): all `400` with **one identical body**. A documents cursor on ledger or audit and every other pair: `400`. An Admin cursor used by Staff2 returns only Staff2's documents/ledger rows (WH02); with `warehouseId=<WH01>` zero rows; `includeTotal` counts only Staff2's scope; a full cursor walk returns each row once (18 of 18). Replay across users therefore cannot widen results: the cursor carries only a position, scope and filters are re-applied on every request.
   - Expiry: none by design. A cursor made before an API restart still works (same key ring: `200`); after switching to an empty key ring it is `400` and a fresh cursor works.
   - Key-ring failures are fail-closed: `ASPNETCORE_ENVIRONMENT=Production` without `DataProtection:KeysPath` aborts start-up with `DataProtection:KeysPath must name a writable directory in Production`; a path that cannot be created aborts with `The key ring for list cursors cannot be used` (`CursorProtectionSetup.VerifyCursorProtection`). Outside Production the temp-folder default is used (documented).
6. **Enumeration / counting** (`enum.mjs`): crawling 23 GET endpoints as 6 roles, the only integer id-like JSON properties are `items[].id` on the ledger and audit (PID-01, PID-04); everything else is a GUID. OpenAPI: the only non-uuid id-like properties are `LedgerRow.id`, `AuditLogDto.id`, `AuditLogDto.entityId` (text, legacy), the cursors and the two concurrency tokens. No `Location` header on creates; `ETag` only on documents. Exports (ledger, stock summary, movements, low stock, import template) contain no ids, only SKU, warehouse code, document number. The insufficient-stock 409 for a product soft-deleted after drafting names the product and warehouse by GUID (PR-6 minor M1 is closed): `"Insufficient stock for product <guid> in warehouse <guid>: available 1, requested 5."`.
7. **Errors and headers**: `X-Correlation-Id` with CR/LF is refused by the client; a 500-character or markup value is replaced by a server-made 32-hex id; no `Server` header; problem details echo only a GUID the caller itself sent (for example `Product <guid> does not exist.`).
8. **Operations**: Production returns 404 for `/openapi/v1.json`, `/swagger`, `/scalar`, `/.env`; CORS from `https://evil.example` gets no `Access-Control-Allow-Origin` (preflight 204 without it), the configured origin is allowed; request logs hold the path only (no query string, so no cursor; searched the log for a cursor prefix: 0 hits; no bearer tokens). The Data Protection folder is created with inherited ACLs (current user, Administrators, SYSTEM).
9. **Web (read only)**: route parameters are validated with `normalizePublicId`; an old numeric bookmark (`/receipts/123`) is a not-found page and sends no request (`document-editor.component.ts:211-220`); the service-worker data group `master-data` was bumped to `version: 2`; local storage holds only `ims.documents.scope.<userId>` (`mine`/`all`), theme, language and a delete-only legacy refresh-token key; no ids other than the user's own GUID are stored; there is no `Number(id)` on routes.

## 4. Informational

- **PID-06** JWT claims carry the caller's own internal keys (`sub` = user int, `wh` = warehouse ints). Own values only, ordinal of the account; accepted by the ADR (section 3.2 item 7).
- **PID-07** A non-GUID path id (`/api/documents/1`) gets the router's generic 404 (`"The requested resource was not found."`), a GUID that is not visible gets the domain 404 (`"Document not found."`). This reveals only whether the value has GUID shape, not existence. The empty GUID gets a 400 "Choose a ..." on create bodies and uploads. No action.
- **PID-08** Document numbers are per warehouse and month (`RCV-WH01-2610-0016`), so a scoped user sees only their own warehouse's counters. No action.
- **PID-09** Unchanged limits worth recalling: a deactivated user's access token lives until it expires; `GET /api/stock/low-stock` and `lowCount` return product-level alerts even to `noscope` (2 of 3, the products with no stock anywhere); this is not warehouse volume. Not part of this change.

## 5. Recommended fixes (in order)

1. PID-01 + PID-04: remove `id` from `LedgerRow` and `AuditLogDto`; empty the `OpenApiDocumentTests` allow-list; regenerate the client. About 1 hour. Owner: be-framework.
2. PID-02: HMAC the document concurrency token (`DocumentVersion.cs`), compare in constant time; add a test that the token is not a function of a counter. About 1 to 2 hours. Owner: be-framework.
3. PID-03: add one runbook line about the `keys` volume (owner-only, no shared backup). be-architect.

After 1 and 2, `#78` is safe to merge from the security side. If the owner prefers to accept PID-01 and PID-02, record it in `adr-guid-keys.md` under D2 ("ledger id and document tokens are position tokens, volume of other warehouses is visible to any stock reader").

## 6. Retest instructions

```bash
# 1. publish and start (Development, new database on (localdb)\ImsDev, seeded demo users), then:
export IMS_BASE=http://localhost:5710 IMS_STATE=/path/outside/repo/state.json
cd tests/security/public-id-retest
node setup.mjs && node softdelete.mjs | sqlcmd -I -S "(localdb)\ImsDev" -E -d <your database>
node idor.mjs out.json; node idor-fields.mjs; node client-ids.mjs; node cursors.mjs
node enum.mjs; node counters.mjs; node timing.mjs
```

Expected after the fixes: `counters.mjs` prints non-monotonic or absent ids (step 2 and 3 deltas no longer equal the number of foreign writes; `enum.mjs` lists no `items[].id`); everything else unchanged.

Cleanup done in this run: database `Ims_PidRetest10` dropped; processes started by recorded PID stopped; scratch key-ring folders are outside the repository.
