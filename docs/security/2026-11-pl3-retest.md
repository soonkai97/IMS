# Security retest: sales orders, price lists, returns, modules and the deploy kit (2026-11-14)

Scope: PL3.1 to PL3.18 and their follow-ups, the module switching, and the merged deploy kit (PR #243 and #252). Reviewer: security-engineer. Basis: origin/main at 039e77b (deploy hardening #252 merged after the review; see `2026-11-deploy-retest-follow-ups.md`).

**Method.** Code read through `git show origin/main:` only. No test suite and no live penetration test were run in this round. The only commands run were `dotnet list package --vulnerable --include-transitive --no-restore` and `npm audit --package-lock-only` on a copy of the lockfiles.

**Result.** No regression and no new application defect. Every earlier finding is closed in the code. What remains are deploy-side gates that cannot be verified without a host.

## Findings

| ID | Topic | Status | Evidence (origin/main) | Residual risk |
|---|---|---|---|---|
| A1 | Authorization matrix of every new endpoint | closed | `SalesOrdersController.cs`, `PriceListsController.cs`, `ReturnsController.cs`, `ModulesController.cs`; `EndpointCatalog.cs` plus `EndpointCoverageTests.cs` force table = code | tests not run in this round |
| A2 | Module off answers 404 before 403 | closed | `ModuleGateMiddleware.cs` between authentication and authorization; anonymous still 401 | one settings query per gated request |
| A3 | Warehouse scope and IDOR | closed | `SalesOrderDtoSpec.cs`, `SalesOrderFilters.cs`, `EnsureInScope` in every order handler; delivery and return creators check scope before the lock | price lists are global by design |
| V1 | Selling price versus cost | closed | `PriceLookupDto`, `PriceListItemDto`, `SalesOrderLineDto`, `AuditFields` (so.manage / pricelist.manage, never cost.view), `AuditPayloadScrubber`, `ResponseShaper`, `HideAmounts` | audit payloads still carry integer ids (accepted) |
| V2 | Return-line cost | closed | server discards a typed cost (`DocumentLineBuilder`, `StockDocumentLine`); the web sends none | none |
| V3 | Exports and Return flag | closed | `ExportMovementsHandler`, `MovementProjection` (no join) | the Return column shows while the module is off (information only) |
| V4 | DTO omission while a module is off | closed | `UserInfoMapper`, `DocumentReader` | only `returns.create` is hidden (contract decision) |
| P1 | Two approval tiers | closed | `ApprovalPolicy`, `ApproveSalesOrderHandler` | sales-order self-approval allowed when the setting is on (by design) |
| P2 | Returns never self-approved | closed | `ApproveDocumentHandler`, `RejectDocumentHandler`, `DocumentAccess`, `ForReviewAsync` | none |
| P3 | Forced Supervisor approval of returns | closed | `ApprovalPolicy.ForReturn`, `ApprovalEvaluator`; a Draft cannot post | none |
| P4 | Value ceiling for returns (D12, owner decision O16) | accepted risk, open | `ApprovalEvaluator` returns no limit for a return | see gate 3 below |
| P5 | Recall and cancel races | closed | If-Match plus row version; the cancel takes the `so` lock; update answers 428 on an empty token | none |
| C1 | Reversal of an original with a Posted return | closed | `ReverseDocumentHandler` takes the locks in the order `so`, `ret`, check | none |
| C2 | Lock order | closed | `ITransactionLocker` documents the order; creation takes modules, then `ret`, then docseq | none |
| C3 | Rechecks under the lock and stable codes | closed | `ReturnPosting` (row-version stamp, over-return, exhausted line), `SalesDeliveryPosting`; codes in `ProblemCodes` | race tests exist, not run in this round |
| M1 | Requires enforced both ways | closed | `SwitchModuleHandler` reads stored rows under the modules lock | none |
| M2 | `returns.create` hidden and web reload after a switch | closed | `UserInfoMapper`, `modules.store.ts`, `auth.service.ts` | none |
| M3 | Role-editor closure | closed | `ReplaceRolePermissionsHandler` checks the merged set | none |
| W1 | Stale command state in the web | closed | reset on every open in the order list, the price-list editor and the document editor; guards check the module first, then the permission | root stores keep the last result in memory after sign-out (information only on a shared device) |
| W2 | Banners built from server data | closed | zero `innerHTML` or `bypassSecurityTrust` hits in non-spec web code | none |
| D1 | Deploy runs from main only, ancestor check | closed in the file; GitHub settings not verifiable | `deploy.yml`, `release.yml` | the environment reviewers and the "main only" branch rule are GitHub settings |
| D2 | Workflow permissions and triggers | closed | `deploy.yml` read-only; `release.yml` is the only `packages: write`; no `pull_request_target` | none |
| D3 | Tag from the built commit | closed | `release.yml`; `deploy.yml` validates the tag | none (the `latest` tag was removed by #252) |
| D4 | Fork publishing | closed | `release.yml` needs event `push` and head repository = this repository | a collaborator with write access can dispatch Release, but it builds only a main commit |
| D5 | Secrets handling | closed in the file | env file mode 600, no password on a command line, token over SSH stdin with a logout trap, key removed with `if: always()`, pinned known_hosts | `docker inspect` shows env values to docker-group members (root-equivalent, accepted) |
| D6 | Network exposure | closed in the file | only the proxy publishes ports, `back` network is internal, `pids_limit` on all services | database keeps default capabilities (unverified) |
| D7 | API connects as `sa` | gate, open | #252 makes `IMS_DB_USER` mandatory and `sa` refused without `--first-migration`; `create-app-login.sql` DENYs update and delete on the two append-only tables | not verified that the app runs under `ims_app` |
| D8 | Backups | closed in the file; restore not verifiable | age encryption, prune deletes only shipped files and keeps the newest fulls, ship takes files older than 10 minutes, failure alert (#252) | `.bak` files are plain on the VM disk; the restore test has not been done |
| D9 | Caddy, TLS, CSP | not verifiable without a host | Caddy was never started | none beyond that |
| D10 | Image and action pinning | partly closed | actions pinned by SHA and `Dockerfile.web` base images by digest (#252) | API Dockerfile and SQL image not pinned |
| S1 | NuGet vulnerabilities | closed | 0 vulnerable packages in 7 projects | none |
| S2 | npm vulnerabilities | accepted risk | production audit 0, e2e 0; full web audit 23 findings, all dev-only (jest and nx chain); `deepmerge` (low, no patch) and `glob` GHSA-3g3m-2fjr-6pw4 (moderate) accepted | do not apply the suggested jest@25 downgrade |
| S3 | Secrets scan | closed | no key, token or connection-string patterns in the tracked tree | none |

## Gates before a pilot with real data (not code defects)

1. API database login: `ims_app` must be proven (post, return post, reversal, module switch and the seeder run under it); `sa` only for the first migration.
2. Deploy integrity rests on GitHub settings that are not in the repo, and `main` needs no approving review today: set environment reviewers with "prevent self-review" plus a second human, confirm "main only" deployment branches, and require 1 review once a second person exists.
3. Value ceiling on returns (owner decision O16): a staff member plus one Supervisor can return a large sale at its original cost without the Owner seeing it. Route large returns to the large tier, or record the owner's written acceptance. The placeholder approval amounts (RM 5,000 and RM 20,000) must also be replaced with real ones.
4. Backups never proven: do one off-host restore of an age-encrypted copy and confirm the provider's disk encryption.
5. The deploy kit has never run: rehearse it on a throw-away VM (images, health checks, read-only filesystem write paths, Caddy, `smoke.sh`) and run the DAST scan against it.

## Pilot statement

- Safe to enable with real users and data (application layer): sales orders, price lists, returns and module switching, once gate 3 is settled.
- Not safe yet (hosting): exposing the deployed stack with real data, until gates 1, 2, 4 and 5 are closed and the seed admin password is emptied after the first start.
- This report is based on reading code and the two dependency audits. No test suite and no live penetration test were run.
