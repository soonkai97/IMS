# PL2.11 purchasing security retest probes

Manual probes (not CI) for the purchasing pilot: purchase orders, approval tiers, receive against an order, reorder suggestions, accounting export, module switches. Report: `docs/reviews/pl2-security-retest/report.md`.
Node 20+, no dependencies. Every script prints `ok` / `FAIL [area] label -> evidence` and appends one JSON line per check to `$RV_OUT/results.jsonl`.

## Set-up (once)

1. Publish the API in Release and start it on loopback against the ONE development database (no scratch database): `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://127.0.0.1:5960` (never port 4200),
   `ConnectionStrings__Default=Server=(localdb)\ImsDev;AttachDbFilename=D:\SqlData\ImsDev\Ims_Dev.mdf;Database=Ims_Dev;Trusted_Connection=True;TrustServerCertificate=True`, `Jwt__Key=<any 32+ characters you choose>`,
   `Cors__Origins=http://localhost:4300`, `Logging__Directory=<folder outside the repository>`.
2. Optional second instance in Production mode on 5961 (same database and key; `DataProtection__KeysPath`, `Storage__RootPath` and `Database__MigrateOnStartup=false` set; `Cors__Origins=https://ims.example.test`): used by `p7` for the error bodies, headers and "Swagger is not served" checks.
3. Environment for the scripts: `RV_BASE` (default `http://127.0.0.1:5960`), `RV_PROD_BASE` (default `http://127.0.0.1:5961`), `RV_ADMIN_PW` and `RV_DEMO_PW` (the passwords of the seeded users, in `src/Ims.Api/appsettings.Development.json`; read them at run
   time, never write them into a file you commit), `RV_JWT_KEY` (the `Jwt__Key` you started the API with: needed to mint tokens for the forged/expired-token probes), `RV_OUT` (scratch folder OUTSIDE the repository). The scripts create their own users with one made-up password (`PROBE_PW` in `lib.mjs`).
4. The login limit is 20 per minute per address: `lib.mjs tokens()` caches the 16 fixture logins for 3 minutes and waits out a refused login; `p7` sleeps between its login-heavy steps (it runs about 7 minutes).

## Order

| Step | Command | Covers |
|---|---|---|
| 0 | `node s0.mjs` | switches `purchasing` and `accounting` ON (remembers how they were), creates the `SEC-` roles, users, supplier, customer, products, `fixtures.json` |
| 1 | `node p1-authz.mjs` | area 1: every purchase-order endpoint x 11 caller kinds (five system roles, `po.read`-only, `document.post` without `po.read`, no rights, anonymous, forged role-claim token, expired), token abuse (alg none, wrong key/audience/issuer, tampered payload) |
| 2 | `node p2-segregation.mjs` | area 2: self-approval (Administrator, Supervisor, Owner-like), tier routing and amount boundaries, cancel and close authority, the known editor-then-approver gap |
| 3 | `node p3-cost.mjs` | area 3: callers without `cost.view` get JSON null and no money in raw bodies (order, list, receipt, documents, suggestions, audit log, triggers, `over_receipt` 409) and cannot set prices |
| 4 | `node p4-scope.mjs` | area 4: warehouse scope, 404 sameness, GUID guessing, timing, foreign ids in bodies, scoped export |
| 5 | `node p5-input.mjs` | area 5: mass assignment, malformed/oversized input, control characters, search and filter injection, correlation id, `If-Match`, accounting export formula injection (unzips the .xlsx) |
| 6 | `node p6-concurrency.mjs` | area 6: parallel posts of the last quantity, cancel/approve/recall/reject races, double submit, stale and missing `If-Match`, partial/auto-close/short/reversal, unit-conversion lock (`SEC_ROUNDS` = rounds, default 8) |
| 7 | `node p8-modules.mjs open` | area 8, stage 1 (open orders exist): modules.manage, switch-off refused with a count, role closure rules |
| 8 | `node p7-disclosure.mjs` | area 7: error bodies, headers, CORS, undocumented routes, session lifetime/rotation/replay/logout, lockout, enumeration (message and timing), change-password, rate limits |
| 8b | `node p7b-timing.mjs` | area 7: username enumeration by login timing (8 samples each, two fresh users; needs only the Administrator) |
| 9 | `node p9-logs.mjs mark` | pushes marker passwords, a TIN and a marker price through the API |
| 10 | `node z-cleanup.mjs orders` | reverses the probes' receipts, cancels/closes every order, checks stock of `SEC-` products is back to 0 |
| 11 | `node p8-modules.mjs off` | area 8, stage 2: switches both modules OFF (as found), module-off matrix (404 for everyone, 401 anonymous), hidden grants, enable guard (restores the two found approval rules by SQL) |
| 12 | `node z-cleanup.mjs people` | switches the throw-away users, roles, partners and products off |
| 13 | `RV_LOGS="<log folders and the API stdout file>" node p9-logs.mjs scan` | greps everything the API logged for passwords, key, tokens, TIN, prices |
| 14 | `bash p9-config.sh [deps]` | area 9: secrets scan of tracked files, Actions/Docker review, and (with `deps`) `dotnet list package --vulnerable`, `npm audit`, GitHub alerts |

## Rules

- Only against your own throw-away API process and the development database; never a shared or production database.
- Everything created is prefixed `SEC-` / `sec<4 characters>`; `z-cleanup.mjs` leaves nothing open, waiting or active. Ledger rows of reversed receipts and audit rows stay (they are history).
- Step 11 needs `sqlcmd` and writes two columns of the two found purchase-order approval rules back by SQL, because turning the enable guard on needs the rules inactive and rules can only be saved while the module is on. It prints the rows before and after.
- Do not run on a database where purchasing is in real use: the probes switch the module on and off.

Note (2026-10-22, RA1 part A1): `GET /api/accounting/export` was removed from the API. The probes that call it (`p1`, `p4`, `p5`, `p7`, `p9`) now get 404 for those calls; run them only for the purchasing parts, or retire the accounting steps before the next retest. `s0` and `p8` still switch the `accounting` module until part A2 removes it.

Note (2026-10-22, RA1 part A2): the `accounting` module is removed from the API too, so `PUT /api/modules/accounting` is a 404. `s0` and `p8` still try to switch it; run the purchasing parts only, or retire the accounting steps before the next retest.
