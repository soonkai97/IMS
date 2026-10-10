# Security review: initial (static, whole backend + delivery pipeline)

Role: security-engineer. Date: 2026-10-09. Mode: defensive static review of the owner's own repository. No server was started, no database created, no traffic sent.
Standards: OWASP ASVS 4.0.3 level 2, OWASP API Security Top 10 (2023), STRIDE.

## 0. Summary

| Severity | Count | IDs |
|---|---|---|
| Critical | 0 | |
| High | 0 | |
| Medium | 13 | SEC-01, 02, 03, 12, 16, 17, 18, 21, 22, 23, 27, 28, 34 |
| Low | 17 | SEC-04 to 10, 13 to 15, 19, 24 to 26, 29 to 31 |
| Info | 5 | SEC-11, 20, 32, 33, 35 |

No unauthenticated data access, no privilege escalation, no cross-warehouse read/write path was found. The core controls hold: every action has a permission policy or the deny-by-default fallback, every by-id document path checks warehouse scope, queries are parameterised, DTOs cannot over-post, errors are opaque, passwords and refresh tokens are hashed. What is left is mostly (a) races and abuse paths around login/refresh, (b) resource-consumption limits, (c) the file features (Excel import, attachments), (d) deployment defaults (TLS, forwarded headers, compose, release pipeline) and (e) PDPA design questions.

**Fix before the first push** (the repository has no commits yet; pushing to `main` triggers `release.yml`):
1. SEC-27: `release.yml` publishes a `latest` image on every push to `main` without waiting for CI. Gate it on CI or limit it to tags.
2. SEC-31: add `storage/`, key/cert/backup patterns to `.gitignore`; delete `bk_tmp.sql` from the repo root.
3. SEC-23: stop shipping `appsettings.Development.json` inside the publish output / image, and make demo seeding refuse non-local databases.
4. SEC-28: bind SQL Server's port to loopback in `docker-compose.yml` (and plan a non-`sa` application login).

Snapshot note: be-framework was editing `src/` while this review ran (the Attachments feature appeared during the review, `GetStockSummary` was rewritten, several be-architect findings were fixed). Findings are against the tree as read on 2026-10-09 around 23:00. Attachments have no integration tests yet.

### Status log

Updated 2026-10-10 (security hardening pass, be-framework). Only the items touched in that pass are listed here; the other findings keep the state described in their rows until their own fix is recorded.

| ID | Status | What was done | Left |
|---|---|---|---|
| SEC-34 | **API done, web pending** | Login and refresh set the refresh token as `HttpOnly; Secure; SameSite=Strict; Path=/api/auth` cookie (`__Secure-` prefix; `__Host-` is impossible with a path scope). Refresh and logout read it. CSRF: SameSite plus required `X-Requested-With: XMLHttpRequest` plus Origin/Referer against `Cors:Origins`. Rotation unchanged. Backward compatible: a body token is still accepted; the body no longer returns it by default (`Auth:ReturnRefreshTokenInBody` defaults to false since the web app uses the cookie; the switch stays for a rollback). Design and the migration steps for the web app: `docs/security/refresh-cookie.md`. | Web app must move to the cookie, then set `Auth__ReturnRefreshTokenInBody=false`. Strict CSP for the static host: `docs/security/web-headers.md`. Shorter refresh life for Admin/Owner not done. |
| SEC-18 | **Done, scanner pluggable** | Images lose EXIF/GPS/XMP/IPTC/comments and any bytes after the end marker; broken images are refused; the PDF active-content check runs before storage; `IUploadScanner` hook (default `NoOpUploadScanner`, fail closed, generic 400 for a refused file). `docs/security/upload-scanning.md`. | Plug in a real AV engine (ClamAV) behind the hook; PDF metadata is kept (documented). |
| SEC-24 | **Done** | `SecurityHeadersMiddleware` on every response (nosniff, `Referrer-Policy: no-referrer`, `Cross-Origin-Resource-Policy: same-site`, `Permissions-Policy`, `X-Frame-Options: DENY`, `default-src 'none'` CSP, `no-store` on `/api/auth/*`); endpoints may override (attachments do). Pinned by `SecurityHeadersTests`. | none |
| SEC-21 | Partly | HSTS is now 1 year with subdomains (outside Development, HTTPS only). Header and TLS configuration for the proxy: `docs/security/web-headers.md`. | TLS termination stays a deployment duty; SQL `TrustServerCertificate` unchanged. |
| SEC-25 | Partly | A wildcard in `Cors:Origins` stops start-up (credentials cannot be combined with `*`). | `AllowedHosts`, https-only origins outside Development. |
| new | **Done** | Weekly authenticated ZAP API scan in CI (`.github/workflows/dast.yml`), fails on high-risk alerts only, accepted findings in `.github/zap/rules.tsv`. `docs/security/dast.md`. | First run to be triaged by security-engineer. |

## 1. Scope and method

Read in full: `src/Ims.Api` (Program, Composition, Common, all controllers), `src/Ims.Application` (Auth, Users, Audit, Documents, Stock, Reports, Import, Attachments, Settings, Warehouses, Partners, Catalog validators, Common), `src/Ims.Domain` (Identity, Attachments, constants, document/product models), `src/Ims.Infrastructure` (Security, Excel, Storage, Persistence mapping, UnitOfWork, Seeder, DI), `appsettings*.json`, `Dockerfile`, `docker-compose.yml`, `.gitignore`, `.github/**`, plus the front-end token storage and service-worker config for the threat model. Existing tests were listed to avoid proposing duplicates.

Ran (read-only):
- `dotnet list <csproj> package --vulnerable --include-transitive` for Ims.Domain, Ims.Application, Ims.Infrastructure, Ims.Api, and for the whole solution: **no vulnerable packages** (sources: nuget.org plus three machine-local feeds, see SEC-30).
- `dotnet list <csproj> package --outdated` for the three projects that have packages: **no updates** available.
- `npm audit` in `web/`: 55 findings (32 high, 23 moderate), **0 with `--omit=dev`** (SEC-32).
- `dotnet test tests/Ims.Architecture.Tests -c Release`: 51 passed. `dotnet test tests/Ims.Domain.Tests -c Release`: 459 passed. Integration tests were not run (they need SQL Server).
- Secrets scan of the working tree (excluding bin, obj, node_modules, .git): no keys, tokens, private keys or connection strings with real passwords. Only dev/CI throwaway values (SEC-33).

Not covered: runtime behaviour (no penetration test yet), the Angular shell (not present in `web/apps/main/src`), infrastructure outside this repo.

## 2. Threat model (STRIDE, ranked by impact)

Trust boundaries: Internet/LAN -> reverse proxy (TLS) -> API container -> SQL Server; API -> local file storage; developer -> GitHub -> GHCR image -> deploy.

| Rank | Component | Asset at risk | Main threats (STRIDE) | Existing mitigations | Gaps (finding) |
|---|---|---|---|---|---|
| 1 | Database | Stock ledger, balances, users, audit | T: edit ledger/audit; I: dump; E: use `sa` | Append-only DbContext guard + triggers on ledger and audit; EF parameterisation, no raw SQL (architecture test); rowversion concurrency; READ_COMMITTED_SNAPSHOT | App uses `sa` and port 1433 published (SEC-28); `TrustServerCertificate=True` (SEC-21); PII frozen in immutable audit (SEC-16) |
| 2 | API | Everything behind auth | S: stolen/guessed credentials; E: role/scope bypass; I: BOLA, verbose errors; D: expensive queries/uploads; R: lost audit | PBKDF2 hashing; 5-strike lockout; JWT iss/aud/sig/lifetime validated; policy per endpoint + deny-by-default fallback; warehouse scope in Application; opaque ProblemDetails; rate limits; request timeouts; Kestrel limits; audit in same transaction | Lockout and refresh races (SEC-01); weak password policy (SEC-02); audit flooding (SEC-03); resource consumption (SEC-12); TLS and forwarded headers (SEC-21/22) |
| 3 | CI/CD and image supply chain | Code integrity, published image | T: unreviewed code reaches `latest`; E: token with `packages: write`; I: secrets in logs | Top-level `permissions: contents: read`; built-in `GITHUB_TOKEN` only; no `pull_request_target`; deploy gated by environment; Dependabot (nuget, npm, actions, docker); NuGet vulnerability gate in CI | Release not gated on CI, tags not SHAs, no image scan or SBOM (SEC-27); no lock files (SEC-30) |
| 4 | File storage (attachments) | Uploaded photos/PDFs, disk space | T: malicious file served to colleagues; I: path traversal, cross-warehouse read; D: disk fill; E: stored script | Type from magic bytes; generated names and path; path-containment check; size enforced while streaming; owner policy (warehouse scope) on every read; `nosniff`, CSP `default-src 'none'; sandbox`, `no-store`, forced download for PDF | No malware scan, header-only validation, EXIF kept (SEC-18); no quota (SEC-12); non-atomic upload (SEC-19); no volume or permissions defined for the container (hardening list) |
| 5 | Browser / PWA | Refresh token (7 days), cached master data | S/I: XSS steals token; I: shared device sees cached data | Access token in memory only; refresh rotates; data caches deleted by `pwa.service.ts:50-51` | Refresh token in `localStorage` (SEC-34); confirm cache purge on logout (SEC-35); no CSP can be reviewed yet (shell absent) |
| 6 | Container | Host, secrets in env | E: root in container; I: secrets in layers | Non-root `$APP_UID`; multi-stage build; secrets from env, `${VAR:?}` fails fast; Swagger off outside Development/Testing; `Server` header off | Dev settings file in image, no `.dockerignore`, floating tags (SEC-23/29); no `read_only`/`cap_drop` (SEC-28) |

## 3. Secure-code review findings

Legend: ASVS = 4.0.3 section; API = OWASP API Security Top 10 2023. File:line are against the tree read on the date above.

### 3.1 Authentication, session, tokens

| ID | Sev | OWASP | Where | Failing case | Fix |
|---|---|---|---|---|---|
| SEC-01 | Medium | API2, ASVS 2.2.1, 3.5.3 | `Ims.Domain/Identity/Models/User.cs:15,59-67`; `Ims.Infrastructure/Persistence/AppDbContext.cs:17-27,33-38`; `LoginHandler.cs:37-42`; `RefreshTokenHandler.cs:12-20` | Neither `User` nor `RefreshToken` has a concurrency token. N parallel wrong-password requests each read `FailedLoginCount = 0` and write 1, so the 5-strike lock (AUTH-01) is bypassed by parallelism (only the 20/min/IP limiter remains). Two parallel refreshes of one token both see `RevokedAt = null`, both revoke, both insert a new token: a stolen refresh token forks into two live sessions. `TokenTests.cs:36-45` passes only when the two requests do not overlap, so it hides this. | Add `RowVersion` to both entities (a conflict becomes 409/401), or make the writes atomic: `ExecuteUpdate` `FailedLoginCount = FailedLoginCount + 1`, and `UPDATE RefreshToken SET RevokedAt = @now WHERE Id = @id AND RevokedAt IS NULL` with a rows-affected check. Make the test deterministic (barrier, many parallel calls, assert exactly one 200). |
| SEC-02 | Medium | API2, ASVS 2.1.1, 2.1.5, 2.1.9 | `Limits.cs:9`; `UserSaveRequestValidator.cs:14,16`; `AuthController.cs:9-30`; `Seeder.cs:52-61`; `docker-compose.yml:25` | Password rule is length >= 8 only (ASVS L2 asks 12, and a block list of common/breached passwords). There is no change-my-password endpoint, so every password is set, and therefore known, by an administrator, and nothing forces a change at first login. The first admin is always `admin` with a password from env that stays in the container environment forever. The seed password skips the policy. | Min length 12, deny list (top 10k plus username/email substrings), `POST /api/auth/change-password` (current password required, revokes other refresh tokens), `MustChangePassword` flag set on admin-created/reset accounts, apply the policy in the seeder, remove `Seed__AdminPassword` after first start (docs). |
| SEC-03 | Medium | API8, ASVS 7.1.1, 7.1.2, 8.3 | `LoginHandler.cs:15,22-24`; `Ims.Application/Features/Audit/Services/AuditTrail.cs:13-18`; `AppDbContext.cs:196-201` + trigger `TR_AuditLogs_AppendOnly` | An anonymous caller makes 20 requests/min/IP, each writing a permanent, trigger-protected `login.failed` row whose `Actor` and `EntityId` are the attacker's text (up to 100 chars). The table can never be pruned, so it is an unauthenticated storage-growth vector. A user who pastes their password into the username box has it stored in clear text and shown to Owner/Admin (`audit.read`). | For an unknown user, audit a constant (`unknown`) plus the first 8 hex of SHA-256(key), never the raw text; or log only (no row) when the key matches no account. Add a per-key and global ceiling on anonymous audit rows, and a retention/partition plan that keeps append-only semantics (archive by partition switch). |
| SEC-04 | Low | API2, ASVS 3.3, 3.7 | `RefreshTokenHandler.cs:12-13`; `RefreshToken.cs:12-17`; `LogoutHandler.cs:9-15` | Replaying an already-rotated token returns 401 but leaves the token family alive (no reuse detection). Expired and revoked rows are never purged. No "log out everywhere". Logout and refresh share one IP limiter. | Store a family id; on reuse revoke the family and audit `token.reuse`. Nightly purge of rows expired > 30 days. Add `POST /api/auth/logout-all`. |
| SEC-05 | Low | API2, ASVS 2.4.1, 2.4.3 | `IdentityPasswordHasher.cs:8,13-14`; `UserSaveRequestValidator.cs:16` | Default PBKDF2 settings (100k, HMAC-SHA512) are below the OWASP 210k guidance; `SuccessRehashNeeded` is treated as success but never rehashed. Update-user accepts a password of any length (create caps at 128), so an admin request can force a multi-MB PBKDF2 input. | Configure `PasswordHasherOptions.IterationCount = 210_000`, rehash on `SuccessRehashNeeded`, add `MaximumLength(Limits.PasswordMaxLength)` on update. |
| SEC-06 | Low | API2, ASVS 3.5, 6.1 | `ConfigureJwtBearer.cs:13-20`; `JwtOptions.cs:8,11`; `TokenService.cs:18-29`; `UpdateUserHandler.cs:23-36` | No `ValidAlgorithms` pin; key strength is "32 characters" (a repeated letter passes); `AccessMinutes` may be configured up to 24 h; no `jti`/`kid`, so no key rotation without logging everyone out; a deactivated or demoted user keeps a valid access token until it expires (refresh is revoked, access is not). | `ValidAlgorithms = [HS256]`; require Base64 key >= 32 random bytes; cap `AccessMinutes` at 30; support two keys (`kid`) for rotation; optional "token version" claim checked against a cached value if the 15-minute window is too long. |
| SEC-07 | Low | ASVS 2.1, 4.1 | `UserByLoginSpec.cs:9`; `UserTakenSpec.cs:9` | Login matches `LOWER(Username)` or `LOWER(Email)`. User A's username may equal user B's email (uniqueness is checked per column), so `FirstOrDefault` (TOP 1, no order) picks one arbitrarily. The `LOWER()` makes both lookups non-sargable (scan on each login). | Reject a username that contains `@`, or check both columns against both values in `UserTakenSpec`; rely on the case-insensitive collation instead of `LOWER()`. |
| SEC-08 | Low | API2, ASVS 2.2.1 | `PresentationServices.cs:77-78`; `LoginHandler.cs:37-42` | Login throttling is per IP only; anyone who knows a username can lock it for 15 minutes with 5 requests (known trade-off). Behind NAT, 20/min/IP is shared by a whole site. | Per-account+IP token bucket, alert on repeated lockouts, optional CAPTCHA-free delay (exponential back-off per account). |

### 3.2 Authorization and object references

| ID | Sev | OWASP | Where | Failing case | Fix |
|---|---|---|---|---|---|
| SEC-09 | Low | API1/API3, ASVS 4.2.1, PDPA | `UsersController.cs:12`; `UserFilters.cs:12-13`; `UsersListSpec.cs:13`; `Permissions.cs:28,31` | `users.read` is held by Owner and Supervisor. A Supervisor scoped to one warehouse lists every user in the company with e-mail, role and warehouse ids. | Return only users who share a warehouse with the caller (Admin/Owner: all), or drop `Email` for non-Admin. Needs a product decision. |
| SEC-10 | Low | API5, ASVS 4.1.3 | `AttachmentsController.cs:15-48`; `DocumentAttachmentPolicy.cs:14,23-28`; `ProductAttachmentPolicy.cs:11-12` | New routes rely on the fallback policy and a per-owner policy; there is no `[Authorize(Policy=...)]`, so `EndpointCoverageTests` cannot list them, and a future `[AllowAnonymous]` or a changed fallback silently opens them. Reading a document's files does not check `stock.read` (all roles hold it today); any `document.create` user in the warehouse may delete any other user's file on a Draft. | Put `[Authorize(Policy = Permissions.StockRead)]` on list/download of documents and `DocumentCreate`/`MasterDataManage` on write/delete by owner type (or one attribute per action plus the owner check); add the routes to `EndpointCatalog`; optionally restrict delete to the uploader or a supervisor. |
| SEC-11 | Info | API1, ASVS 4.2.1 | `DocumentReader.cs:26-27`; `DocumentAttachmentPolicy.cs:32-33` | An id outside the caller's scope returns 403, a missing id 404: existence oracle (ids are sequential integers anyway). | Return 404 for out-of-scope ids in by-id reads. |

### 3.3 Input validation, mass assignment, resource consumption

| ID | Sev | OWASP | Where | Failing case | Fix |
|---|---|---|---|---|---|
| SEC-12 | Medium | API4, ASVS 12.1, 13.1 | `ServerLimitsSetup.cs:15` + `Limits.cs:40`; `Paging.cs:9` + `Limits.cs:77`; `ExportStockSummaryHandler.cs:11`; `ExportLowStockHandler.cs:11`; `PresentationServices.cs:79-80`; `AttachmentsController.cs:22` | (1) Every JSON endpoint accepts a 12 MB body. (2) `page=100000&pageSize=200` is an OFFSET of 20M rows plus a COUNT per call. (3) Stock summary and low-stock exports load every balance row into memory (`paged:false`, no `MaxExportRows`), callable by Owner/Supervisor/Admin. (4) Uploads: 10 MB each, 20 per owner, but owners are unlimited and the only global limit is 600 requests/min, so one account can push GBs per minute and fill the disk. | Default body limit 256 KB, raise per action (upload, import). Clamp OFFSET (cap page x size at e.g. 50,000, use `afterId` beyond it) and drop the COUNT for deep pages. Apply `Take(Limits.MaxExportRows)` to the two stock exports. Per-user upload bucket (e.g. 20 files/min, 200 MB/day) and a global free-space guard in `LocalFileStorage`. |
| SEC-13 | Low | API3, ASVS 5.1.3 | `PartnerSaveRequestValidator.cs:9-12`; `WarehouseSaveRequestValidator.cs:9-13`; `AppDbContext.cs:68-74`; `UomSaveValidator.cs:7`; `AppDbContext.cs:45`; `RefreshTokenValidator.cs:6`; `UserFilters.cs:12-13`; `StockFilters.cs:72-73`; `DocumentFilters.cs:68`; `ProductsFilterSpec.cs:12-13` | `Partner.Contact`, `Warehouse.Contact` and `Warehouse.Notes` are `nvarchar(max)` with no validator (megabytes per row from a Supervisor). `Uom.Name` > 100 chars hits SQL truncation -> opaque 500. Refresh/logout token strings and all `Search` filters are unbounded; `Limits.SearchMaxLength` exists but is not used. | `MaximumLength` on every string DTO field (map the same constants in `AppDbContext`), `MaximumLength(128)` on tokens, `Search` <= `Limits.SearchMaxLength` in every filter. Add an architecture test: every `string` property of a request record has a length rule. |
| SEC-14 | Low | API3, ASVS 5.1.4 | `DocumentSaveRequestValidator.cs:71`; `ProductSaveRequestValidator.cs:19,21`; `ApprovalRuleSaveRequestValidator.cs:35-36`; `ImportOpeningStockHandler.cs:36`; `ImportProductsHandler.cs:61` | Unit cost, safety stock, conversion factor and approval thresholds have a lower bound but no upper bound or scale. `1e20` overflows `decimal(18,x)` (SQL 8115 -> 500) or `qty * cost` (OverflowException -> 500). | `LessThanOrEqualTo(max)` plus `PrecisionScale` matching `SqlTypes.Cost/Money/Quantity`; same checks in the import row validation. |

### 3.4 Injection, logging, error handling

| ID | Sev | OWASP | Where | Failing case | Fix |
|---|---|---|---|---|---|
| SEC-15 | Low | ASVS 7.3.1, CWE-117 | `AuditTrail.cs:18`; `LoginHandler.cs:22`; `LoginValidator.cs:10-11`; `LogTemplates.cs:6-7` | The login username is only length-checked, then written to the log through `{Actor}`. Serilog's `{Message:lj}` does not escape CR/LF, so `"x\n2026-10-09T00:00:00 [INF] ... login.success admin"` forges a log line in console and file. (The correlation id is sanitised correctly: `CorrelationIdMiddleware.cs:11-12`.) | Reject control characters in username/e-mail (validator), strip `char.IsControl` before logging free text, or use a JSON formatter for the file sink. |
| SEC-16 | Medium | ASVS 8.3.4, PDPA retention principle (s.10) and correction right (s.34) | `AppDbContext.cs:169-176` | `User` rows are audited with e-mail and display name in `Before/After`, in a table made immutable by trigger. A correction or erasure request under PDPA cannot be honoured, and former-employee data lives forever. | Audit only id, username, role, active flag and scope for `User`; add `Email`/`DisplayName` to `AuditSkippedProperties`, or store a salted hash. Decide a retention period for audit data and document the legal basis. |

Held up in this area: no raw SQL (`DataAccessSafetyTests` enforce it); `Contains/StartsWith` on parameters become `CHARINDEX`/`LIKE` with escaping in EF Core, so `%` and `_` in a search are literal; `ProblemDetails` for 500 is opaque with a reference (`ApiExceptionHandler.cs:68`); SQL errors are mapped only for known numbers (`SqlExceptionMapper.cs:19-27`); EF parameter values are not logged (`SensitiveDataLogging` is not enabled).

### 3.5 File handling

| ID | Sev | OWASP | Where | Failing case | Fix |
|---|---|---|---|---|---|
| SEC-17 | Medium | API4, ASVS 12.1.1, 12.2.1 | `ClosedXmlExcelReader.cs:13-14,20-21,23-25,30`; `ImportController.cs:21-22,30-32` | The 5 MB limit is on the compressed file. A small `.xlsx` can expand to gigabytes of sheet XML; ClosedXML loads the whole workbook before the 5000-row check, so one request from a Supervisor can exhaust memory. `GetFormattedString()` can evaluate formulas that carry no cached value (to be confirmed at runtime), so a crafted workbook may burn CPU for the whole 120 s timeout. Duplicate header names throw `ArgumentException` in `ToDictionary` -> 500. `catch { }` at line 14 hides the real exception type. | Before ClosedXML: open the package with `ZipArchive`, reject if the sum of `Length` > 50 MB, entries > 200, or any ratio > 100. Reject cells with formulas (`cell.HasFormula`) as row errors, read `CachedValue`, never recalculate. De-duplicate headers into a row error. Limit columns read (e.g. 30). Catch only `InvalidDataException`/`FileFormatException`. |
| SEC-18 | Medium | API3/API8, ASVS 12.2.1, 12.3, 12.5 | `AttachmentRules.cs:15-23`; `UploadAttachmentHandler.cs:38-46`; `AttachmentsController.cs:36-39` | Type is decided from the first bytes only, so a file that starts with `%PDF-` or JPEG magic but carries script, launch actions or an HTML body is stored and later handed to colleagues (PDF is a download, which the colleague opens locally). Nothing scans for malware and images keep EXIF, including GPS of the worker's phone (PDPA). Mitigated well by `nosniff`, CSP sandbox, forced download for PDF and a generated server-side name. | Re-encode images (strip metadata) and reject files that fail to decode; validate PDF structure and refuse `/JavaScript`, `/Launch`, `/EmbeddedFile`; hook an AV scan (ClamAV or the platform scanner) behind `IFileStorage` with a quarantine state; keep PDF download-only. |
| SEC-19 | Low | ASVS 12.4 | `UploadAttachmentHandler.cs:24-34`; `LocalFileStorage.cs:28,48-53` | The attachment row is committed at line 24, the audit at line 27. If the second save fails, the `catch` deletes the file but the row stays: a dangling record whose download answers 404/500. A crash between `.tmp` write and move leaves `.tmp` files forever. | Wrap in `uow.ExecuteAsync` with one save (audit row and entity together); a start-up/nightly sweeper for `*.tmp` older than 1 day and for files with no row. |
| SEC-20 | Info | ASVS 12.3.5 | `AttachmentRules.cs:49-50` | `SafeDisplayName` removes `char.IsControl` but keeps Unicode format characters such as U+202E (right-to-left override), so a display name can read as another extension. | Also strip categories Cf, Zl, Zp. |

Held up in this area: the export writer stores user text as text cells (`ClosedXmlExcelWriter.cs:29-30`), so `=cmd|...` is never a formula; storage path is generated and contained (`LocalFileStorage.cs:25-26,72-78`); size is enforced on streamed bytes, not on the client's length (`LocalFileStorage.cs:41-42`); inline display is images only and PDF is never inline (`AttachmentsController.cs:39`).

### 3.6 Configuration and hardening of the API process

| ID | Sev | OWASP | Where | Failing case | Fix |
|---|---|---|---|---|---|
| SEC-21 | Medium | API8, ASVS 9.1.1, 14.4.5 | `PipelineSetup.cs:14-48`; `Dockerfile:20`; `docker-compose.yml:23,27` | No `UseHttpsRedirection`/`UseHsts`; the image serves plain HTTP on 8080 and compose publishes it. Access and refresh tokens travel in clear unless a TLS proxy is in front; the API-to-SQL link uses `TrustServerCertificate=True`, which accepts any certificate. | Document TLS termination as mandatory; when `ForwardedHeaders` is on, enable `UseHsts` (max-age 1 year) and redirect on `X-Forwarded-Proto=http`; use a trusted SQL certificate and `Encrypt=True;TrustServerCertificate=False` outside local compose. |
| SEC-22 | Medium | API8, ASVS 14.5.3 | `PresentationServices.cs:89-99` (clears at 95-96); `PipelineSetup.cs:16`; `docker-compose.yml:27` | `ForwardedHeaders:Enabled=true` trusts `X-Forwarded-For` from any sender (known proxies and networks cleared). Because the API port is published directly in compose, a client that talks to 8080 sets its own address, so the per-IP login limiter (20/min) and global limiter can be bypassed by rotating the header. | Configure `KnownProxies`/`KnownIPNetworks` from settings and refuse to start with the flag on and no entries; do not publish 8080 when a proxy is used (bind to the proxy network). |
| SEC-23 | Medium | API8, ASVS 14.1, 2.10 | `Program.cs:27-30`; `Seeder.cs:27,64-90`; `PipelineSetup.cs:37`; `Dockerfile:11-12`; `src/Ims.Api/appsettings.Development.json` | Environment name decides demo behaviour: with `ASPNETCORE_ENVIRONMENT=Development` the app migrates, seeds one demo user per role with the known passwords from the committed file, and exposes Swagger; `Testing` also exposes Swagger and uses a random JWT key. `dotnet publish` copies `appsettings.Development.json` into the image, and there is no `.dockerignore`. A mis-set variable on a reachable host hands out every role. | Seed demo data only when the connection string targets `(localdb)`/`localhost`/`127.0.0.1` and `Seed:DemoData=true`; mark the file `CopyToPublishDirectory=Never`; add `.dockerignore`; limit Swagger to Development and gate `Testing` behind an explicit flag; log a loud warning when Development runs on a non-loopback URL. |
| SEC-24 | Low | API8, ASVS 14.4 | `PipelineSetup.cs` (no header middleware); `AuthController.cs:12-18` | JSON responses carry no `X-Content-Type-Options`, `Cache-Control: no-store` (login and refresh answers hold tokens), `Referrer-Policy`, `Cross-Origin-Resource-Policy`. Only attachments set headers. | Small middleware for `nosniff`, `no-store` on `/api/auth/*`, `Referrer-Policy: no-referrer`. |
| SEC-25 | Low | API8, ASVS 14.5 | `appsettings.json:26`; `PresentationServices.cs:59-65` | `AllowedHosts: "*"`; CORS accepts any string from config, including `*` or an `http://` origin, in Production. | Set `AllowedHosts` per environment; validate origins at start-up (https, no wildcard) outside Development. |
| SEC-26 | Low | API4, ASVS 14.1 | `PipelineSetup.cs:47`; `StorageHealthCheck.cs:95-104`; `InfrastructureHealthChecks.cs:11-12` | `/health` is anonymous and, per call, queries the database and writes and deletes a file in storage; reachable by anyone who reaches the port. | `/health/live` (no dependencies, anonymous) and `/health/ready` on an internal port or with a secret header; probe storage without writing (free-space and directory check). |

### 3.7 Supply chain, CI/CD, containers, repository

| ID | Sev | OWASP | Where | Failing case | Fix |
|---|---|---|---|---|---|
| SEC-27 | Medium | CICD-SEC-1/3/4, ASVS 14.2 | `.github/workflows/release.yml:3-11,28-55`; `deploy.yml:43`; `ci.yml:137-150` | `release.yml` runs on every push to `main` and pushes `latest` to GHCR with `packages: write`, independent of CI: a red build or an unreviewed commit still produces the image the deploy workflow offers. Third-party actions (`docker/*`) are pinned to major tags, not commit SHAs. No image scan, SBOM or signature. `deploy.yml:43` interpolates `${{ secrets.GITHUB_TOKEN }}` and `github.actor` directly in the script. The CI vulnerability gate greps text output, has no npm audit and no CodeQL/secret-scanning. Good: top-level `contents: read`, no `pull_request_target`, built-in token only. | Make release `needs` CI (reusable workflow or `workflow_run` on success) and tag-only for `latest`; pin `docker/*` actions to SHAs (Dependabot keeps them fresh); add Trivy/Grype on the built image, `anchore/sbom-action`, cosign keyless signing; pass secrets through `env:`; add `npm audit --omit=dev --audit-level=high` and CodeQL to CI; require reviews on `main` (branch protection). |
| SEC-28 | Medium | CICD, ASVS 14.1.3, 14.3 | `docker-compose.yml:9,23,25-27` | SQL Server's port 1433 is published on all host interfaces with the `sa` password; the API itself logs in as `sa` (full server control), with certificate validation off. `Seed__AdminPassword` stays in the container environment. No `read_only`, `cap_drop: [ALL]`, `no-new-privileges`, memory/CPU limits, or volume for `Storage:RootPath`/logs. | `ports: ["127.0.0.1:1433:1433"]` or drop the mapping; create an application login with `db_datareader/db_datawriter` (and `db_ddladmin` only for the migration run), see hardening list; add the container options; mount storage and logs as volumes owned by the app UID. |
| SEC-29 | Low | ASVS 14.2.4 | `Dockerfile:3,11,14`; `docker-compose.yml:5` | Base images by floating tag (`10.0`, `2022-latest`), not digest; no `.dockerignore`, so `COPY src/ src/` also copies `bin`, `obj`, `logs/*.log` and local dev files into the build stage. | Pin digests (Dependabot docker ecosystem updates them); add `.dockerignore`. |
| SEC-30 | Low | ASVS 14.2.1 | repo root (no `nuget.config`, no `packages.lock.json`); dotnet output lists three machine-local feeds | Package source is whatever the machine has configured (nuget.org plus local DevExpress and SDK feeds). No package source mapping or lock file: dependency-confusion and silent version drift risk. | Add `nuget.config` (`<clear/>`, nuget.org only, `packageSourceMapping`), `RestorePackagesWithLockFile`, and `dotnet restore --locked-mode` in CI and Dockerfile. |
| SEC-31 | Low | ASVS 14.1 | `.gitignore`; `bk_tmp.sql` (repo root) | `.gitignore` covers `.env`, logs, `*.local.json`, `appsettings.Production.json`, but not `storage/` (Development default folder is `src/Ims.Api/storage`, `DependencyInjection.cs:80`), `*.pfx *.pem *.key *.p12 *.snk`, `*.bak *.bacpac`, other `appsettings.<Env>.json`. `bk_tmp.sql` is a local backup helper with a machine path; no secret, but it should not be committed. `src/Ims.Api/logs/*.log` exists in the tree (ignored). | Add those patterns, delete `bk_tmp.sql`, and add a pre-push secret scan (gitleaks in CI is the cheapest). |
| SEC-32 | Info | ASVS 14.2 | `web/package.json` | `npm audit`: 55 findings (nx, jest, stylelint, lint-staged, axios, brace-expansion, node-forge and their transitive packages), all dev tooling; production dependency set: 0. | Update the tooling in a maintenance PR; keep `--omit=dev` gate in CI. |
| SEC-33 | Info | ASVS 2.10.4 | `.github/workflows/ci.yml:20,78-79,91-92`; `docs/dba/02-measure-api.ps1:18` | Throwaway CI database and demo passwords are literals in the workflow; the DBA script defaults to the dev demo password. Intended dev/CI-only values, no production exposure. | None required; do not reuse these values anywhere else. Rotate the dev values if the repo is ever made public. |

### 3.8 Cross-lane (front end), for fe-architect / fe-framework

| ID | Sev | Where | Finding | Fix |
|---|---|---|---|---|
| SEC-34 | Medium | `web/libs/core/identity/src/lib/services/token-storage.ts:12-28` | The 7-day refresh token sits in `localStorage`, readable by any script that runs in the page (XSS, a compromised dependency). Rotation limits replay but a stolen token is a full session. | Strict CSP (no inline script, `script-src 'self'`), Trusted Types, SRI; or an httpOnly `SameSite=Strict` cookie scoped to `/api/auth` (needs an API change, tell be-architect); shorten refresh life to 24 h for Admin/Owner. |
| SEC-35 | Info | `web/apps/main/ngsw-config.json:21-40`; `web/libs/core/pwa/src/lib/pwa.service.ts:50-51` | The service worker caches authenticated GETs (products, partners incl. contact, warehouses, settings) for 12 h. `pwa.service.ts` deletes the data caches; verify it runs on logout and on session expiry, and that `partners` (contact data) really needs offline caching. | Test on a shared device: login A, logout, login B, confirm no A data. |

## 4. Coverage against the checklists

ASVS 4.0.3 L2 chapters: V2 authentication (SEC-01, 02, 04, 05, 07, 08); V3 session (SEC-01, 04, 06, 34); V4 access control (held up, SEC-09, 10, 11); V5 validation (SEC-13, 14); V7 logging (SEC-03, 15, 16); V8 data protection (SEC-16, 18); V9 communications (SEC-21); V12 files (SEC-17, 18, 19, 20); V13 API (SEC-12); V14 configuration (SEC-21 to 31). V6 cryptography: hashing and signing choices are standard, see SEC-05/06; V10/V11 (malicious code, business logic): business-rule enforcement lives in the domain with tests.

API Top 10 (2023): API1 BOLA held up for documents, attachments, stock, reports (scope tested; SEC-11 info); API2 SEC-01/02/04/05/06/08; API3 mass assignment held up (all inputs are DTO records, no entity binding), SEC-13/14 for bounds; API4 SEC-12/17; API5 BFLA held up per policy matrix, SEC-10 for the new routes; API6 sensitive business flows: bulk import and posting protected by permission and rowversion; API7 SSRF: none, the API makes no outbound calls; API8 SEC-21 to 26; API9 improper inventory: no Swagger outside Development, no delete routes (tested), no versioned duplicates; API10 unsafe consumption: Excel is the only external input (SEC-17).

## 5. What held up (so nobody removes it)

- Authorization: one policy per permission, `RequireAuthenticatedUser` fallback; every by-id document handler calls `EnsureWarehouse` before acting; list, stock, ledger, movements, low stock and exports all take the scope in the specification; an empty scope for a non-admin returns nothing (fail closed); warehouse update checks scope.
- Authentication hygiene: same message for unknown, inactive and wrong password; dummy hash for unknown users; lock disclosed only to someone who knows the password; refresh tokens stored as SHA-256, rotated, unique; role/scope/active/password change revokes refresh tokens and refuses removing the last admin or oneself.
- JWT: issuer, audience, signature, lifetime validated; key length checked at start (`ValidateOnStart`); ephemeral key only in Development/Testing.
- Pipeline order: authentication before rate limiter before authorization; CORS headers survive error responses (tests exist).
- Errors: 500 opaque, curated exception messages only (`ExceptionMessageTests`), concurrency details hidden.
- Data: ledger and audit append-only in code and by trigger; parameterised queries only.
- Logging: no tokens or passwords logged; `PasswordHash` and lockout counters excluded from audit payloads; correlation id sanitised.
- Attachments: generated storage names, containment check, streamed size cap, magic-byte type, defensive response headers.

## 6. Security regression tests to implement

Owner: integration-test-engineer (xUnit on real SQL Server, `tests/Ims.Api.Tests`) and unit-test-engineer (architecture tests, `tests/Ims.Architecture.Tests`). [exists] marks behaviour already covered; extend rather than duplicate. Each line is given / when / then.

Implemented 2026-10-10: 39 (`UploadSecurityTests`), 43 headers part (`SecurityHeadersTests`), the polyglot half of 34 (a file that only starts like an image is refused), plus new cookie/CSRF cases (`RefreshCookieTests`).

**Authentication and session**
1. Given one account with 5 wrong-password attempts sent in parallel (>= 10 concurrent requests), when the lock window is read, then the account is locked and the correct password is refused. (SEC-01)
2. Given one valid refresh token, when 10 refresh calls with it run in parallel behind a barrier, then exactly one returns 200 and the rest 401, and exactly one new token row exists. (SEC-01; replaces the timing-dependent `TokenTests.cs:36`)
3. Given a rotated refresh token is presented again, when the call is made, then the response is 401 and every live token of that user is revoked and `token.reuse` is audited. (SEC-04)
4. Given a user changes their password, when they call change-password with a wrong current password, then 400/401; with the right one, other refresh tokens stop working. (SEC-02)
5. Given a new or reset account has `MustChangePassword`, when it calls any endpoint but change-password and `me`, then 403 with a stable code. (SEC-02)
6. Given a password of 11 characters, or one on the deny list, when an admin creates a user, then 400 naming the rule; a 129-character password on update is 400. (SEC-02, SEC-05)
7. Given a JWT with `alg: none`, an HS384 token signed with the right key, and a token with a wrong `kid`, when each is sent, then 401. (SEC-06) [partly exists in TokenTests]
8. Given `Jwt:AccessMinutes` of 1440 or a key of 32 identical characters, when the host starts, then start-up fails. (SEC-06)
9. Given a deactivated user's still-unexpired access token, when `GET /api/auth/me` is called, then the documented outcome holds (200 until expiry, or 401 if token versioning is added) and the refresh token is dead. (SEC-06) [partly exists]
10. Given usernames "A@x.com" (user 1) and an e-mail "a@x.com" (user 2), when an admin creates the second, then 409. (SEC-07)
11. Given a login with an unknown user name containing a newline and text, when the call finishes, then the captured log output has no extra line, and no audit row contains the raw text. (SEC-03, SEC-15)
12. Given 50 failed logins for 50 distinct unknown names, when the audit table is counted, then at most the configured ceiling of anonymous rows was added. (SEC-03)

**Authorization, BOLA, mass assignment**
13. Given every route in the OpenAPI/controller catalogue, when checked, then each non-auth action has an explicit `[Authorize(Policy=...)]` or is listed in an allow-list, including `/api/attachments/*`. (SEC-10) [`EndpointCoverageTests` pattern]
14. Given a document attachment in warehouse A and a Clerk scoped to B, when listing, downloading and deleting it by id, then 403 (or 404 after SEC-11) for all three, and the bytes are never returned. (API1)
15. Given a product attachment, when a role without `masterdata.manage` uploads or deletes, then 403; a role with it succeeds. (API5)
16. Given an attachment on a Posted document, when delete is called, then 409; on a Cancelled document, upload is 409. [policy rules]
17. Given a request body for user create/update with extra properties (`id`, `passwordHash`, `failedLoginCount`, `lockoutUntil`, `isActive` on create where not allowed), when sent, then the stored user ignores them. (API3)
18. Given a Supervisor scoped to warehouse A, when listing users, then only users sharing a scope are returned and no e-mail of others (once SEC-09 is decided). (SEC-09)
19. Given an out-of-scope document id and a non-existing id, when read, then the status codes are identical (after SEC-11).
20. Given a scoped Supervisor, when exporting stock summary, low stock, ledger and movements, then no row of another warehouse appears. [partly exists]

**Input validation and resource limits**
21. Given strings at max+1 length for every text field of every request record (partner contact, warehouse contact/notes, UoM name, search, token), when posted, then 400 with a field error, never 500. (SEC-13) Plus an architecture test: every `string` property of a request record has a `MaximumLength` rule (or an allow-list entry).
22. Given unit cost, safety stock, conversion factor and thresholds at `1e20`, `-1`, and 7 decimals, when posted (JSON and import), then 400. (SEC-14)
23. Given a JSON body of 300 KB to a normal endpoint, when posted, then 413; an upload of 10 MB + 1 byte, then 413/400. (SEC-12)
24. Given `page=100000&pageSize=200` on documents, stock, ledger and users, when called, then the response arrives within the request timeout or is refused with 400 and a pointer to `afterId`. (SEC-12)
25. Given 100k balance rows (perf fixture), when the stock summary and low-stock exports run, then rows are capped at `MaxExportRows` and memory stays under a set bound. (SEC-12)
26. Given 21 uploads within a minute by one user, when sent, then the 21st is 429; given the daily byte quota is exhausted, then 429/409. (SEC-12)
27. Given `Search` with `%`, `_`, `[a]`, 101 characters, when used on products, users, partners, documents, stock, then wildcard characters match literally and the long value is 400. (SEC-13)
28. Given an `X-Correlation-Id` with CR/LF, quotes, 65 characters or Unicode, when sent, then the response header is a fresh id. [exists]

**Files: Excel import**
29. Given an `.xlsx` whose uncompressed size exceeds 50 MB (generated in the test from repeated empty cells), when imported, then 400 before the workbook is loaded and the process memory delta stays small. (SEC-17)
30. Given a workbook with a formula cell (`=1+1`, `=HYPERLINK(...)`), when previewed, then that row is a validation error; no formula result is imported. (SEC-17)
31. Given a header row with two equal column names, when imported, then 400 with a message, not 500. (SEC-17)
32. Given a cell `=cmd|' /C calc'!A0` as product name, when imported and then exported, then the exported cell is a string-typed cell (inspect the sheet XML `t="s"`/inline string, no `<f>`). (formula injection guard) [writer is safe; pin it]
33. Given a `.xls`, a renamed `.zip`, and an empty file, when imported, then 400 with the same message and no stack in the body.

**Files: attachments**
34. Given a text file renamed `x.jpg`, an HTML file renamed `x.pdf`, an SVG, and a file with a JPEG header followed by `<script>`, when uploaded, then the first three are 400; the polyglot is either rejected (after SEC-18) or served with `nosniff`, `Content-Disposition: attachment`, CSP `sandbox` and the stored content type from magic, never from the client. (SEC-18)
35. Given the file name `../../evil.pdf`, `..\\x`, `a‮fdp.exe`, a 500-char name and `CON`, when uploaded, then the stored path is `yyyy/MM/<guid>.<ext>` and the display name is sanitised (no separators, bidi or control characters). (SEC-20)
36. Given a download, when the response is inspected, then it has `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`, a CSP of `default-src 'none'; sandbox`, and `inline=true` on a PDF still downloads. [pin existing behaviour]
37. Given the database save fails after the file is written (inject a failing audit save), when the request ends, then no row and no file remain. (SEC-19)
38. Given a storage path read from the database that contains `..`, when `LocalFileStorage.OpenReadAsync` runs (unit test), then it throws and opens nothing. [pin existing check]
39. Given an uploaded JPEG with GPS EXIF, when downloaded after SEC-18, then the EXIF block is absent.

**Errors, logging, configuration**
40. Given an unexpected exception, a SQL timeout, and malformed JSON, when each is triggered, then the body has no stack, SQL text, file path or inner message. [exists]
41. Given Production with no `Cors:Origins`, no `Jwt:Key`, no `Storage:RootPath`, or `ForwardedHeaders:Enabled=true` without known proxies, when the host starts, then start-up fails with a clear message. (SEC-22, SEC-25)
42. Given `ASPNETCORE_ENVIRONMENT=Production`, when the app runs, then `/swagger` and `/openapi/*.json` are 404 and no demo user can log in. Given Development with a non-local connection string, then demo seeding is skipped. (SEC-23)
43. Given any response of the API, when headers are read, then there is no `Server` header, `X-Content-Type-Options: nosniff` is present, and `/api/auth/*` carries `Cache-Control: no-store`. (SEC-24)
44. Given 25 login requests from one address, when sent, then the 21st is 429; with a spoofed `X-Forwarded-For` and no trusted proxy configured, the limit still applies to the socket address. (SEC-22) [first half exists]
45. Given a login, refresh, user update and attachment upload, when the log sink output is scanned, then it contains no password, token, refresh token or authorization header value.

**Architecture and repository tests**
46. Every controller action carries an explicit authorization attribute or is on a named allow-list (anonymous list: login, refresh, logout, health). (SEC-10)
47. No `[AllowAnonymous]` outside the allow-list; no `Process.Start`, `HttpClient` construction in `src/` (no SSRF surface); no `Path.Combine` with a user-supplied value outside `LocalFileStorage`.
48. No string property named `*Password*`, `*Token*`, `*Hash*` appears in a DTO returned by a controller.
49. A repository test reads the committed files and fails on private-key headers, `Password=` followed by a literal in connection strings, or JWT-shaped strings (allow-list for the documented dev/CI values).
50. A workflow test (script in CI) fails when a workflow lacks a top-level `permissions:` block or uses a third-party action without a SHA.

## 7. Production hardening checklist

TLS and network
- [ ] TLS 1.2+ terminates at a reverse proxy/load balancer with a managed certificate; the API port is reachable only from the proxy; HSTS (1 year, includeSubDomains) on the proxy and in the app behind it.
- [ ] `ForwardedHeaders:Enabled=true` with explicit `KnownProxies`/networks; real client IP appears in logs and rate limits.
- [ ] SQL Server not exposed to the internet; `Encrypt=True;TrustServerCertificate=False` with a certificate the API trusts; firewall to the API host only.
- [ ] CSP, `frame-ancestors 'none'`, Referrer-Policy and HSTS set on the static host serving the PWA (copy-paste Caddy and nginx config: `docs/security/web-headers.md`).

Secrets and keys
- [ ] `Jwt:Key` (>= 32 random bytes), DB password, `Seed:AdminPassword` come from a secret store (Key Vault, Docker/Kubernetes secrets), never from the repo or image; remove the seed password after first start.
- [ ] Rotation plan: JWT key every 6-12 months or on suspicion, using two keys with `kid` so sessions survive; DB password every 12 months; rotate on staff leaving.
- [ ] Application SQL login is not `sa`: `db_datareader`, `db_datawriter` at runtime, a separate migration login with `db_ddladmin`; no `CONTROL SERVER`.
- [ ] Production never runs with `ASPNETCORE_ENVIRONMENT=Development` or `Testing`; `Database:MigrateOnStartup` is a deliberate, single-instance job.

Data protection and PDPA (Malaysia)
- [ ] Inventory of personal data: users (name, e-mail, role, warehouses), audit rows (actor names, e-mails per SEC-16), logs (username, client IP), attachments (photos may contain people, locations, EXIF), partner/warehouse contact fields.
- [ ] Notice and consent/purpose statement for staff; named data-protection contact; process for access and correction requests (needs the audit change in SEC-16).
- [ ] Retention: logs 30 days (set), refresh-token rows 30 days after expiry, audit 7 years for financial records or the legal minimum, attachments per document policy; documented deletion job.
- [ ] Encryption at rest for the database volume, the attachment folder and backups; backups are access-controlled.
- [ ] Cross-border transfer check if the cloud region is outside Malaysia; breach notification runbook (PDPA amendments 2024 require notifying the Commissioner).
- [ ] Minimise: supervisors see only their users (SEC-09); do not log request bodies.

Backups and recovery
- [ ] Nightly full + log backups with `CHECKSUM`, copy off-host, restore test each quarter (the repo's `bk_tmp.sql` shows the pattern; move it under `scripts/` with a proper path).
- [ ] The attachment folder is backed up in the same cut as the database (point-in-time consistent) and restored together.
- [ ] RPO/RTO written down and tested.

Logging, monitoring, response
- [ ] Ship logs to a central store with access control; alert on repeated `login.lockout`, `token.reuse`, 5xx spikes, 429 spikes, upload rejections, audit-table growth.
- [ ] Log retention 30-90 days online; audit table partitioned by month for archival; time sync on hosts.
- [ ] Incident runbook: revoke all refresh tokens (`UPDATE RefreshToken SET RevokedAt = ...`), rotate JWT key, disable user.

Runtime and containers
- [ ] Image from a gated pipeline, scanned (Trivy), signed, pinned by digest; `read_only` root filesystem, `cap_drop: ALL`, `no-new-privileges`, memory/CPU limits, volume for storage and logs owned by the app UID with mode 0700.
- [ ] Health endpoints split into live/ready; ready not public.
- [ ] Upload quota, disk-space alert at 80 %, AV scan for attachments.

Dependency and update policy
- [ ] Dependabot weekly (NuGet, npm), monthly (Actions, Docker); security updates merged within 7 days (Critical/High within 48 h).
- [ ] CI blocks on `dotnet list package --vulnerable --include-transitive` (exists), `npm audit --omit=dev --audit-level=high` (add), CodeQL and a secret scan (add).
- [ ] `.NET` patch releases adopted within 14 days of release; `global.json` rollForward stays on latestFeature.
- [ ] Lock files and `--locked-mode`; a single package source with mapping.

Access governance
- [ ] Branch protection on `main` (PR review, CI required, no force push), CODEOWNERS enforced, environment approval for production deploy (already planned in `deploy.yml`).
- [ ] Admin accounts: named individuals, 12+ character passwords, quarterly review of Admin/Owner list and audit of `user.updated` events.

## 8. Retest

After be-framework fixes: re-run the commands in section 1; implement the tests in section 6; then ask the security-engineer for a runtime pass (Release build against a throw-away `Ims_Pentest` database, per the rules of engagement) covering SEC-01, 12, 17, 18, 22 with reproducible scripts in `tests/security/`.
