# Security probes

Manual proof-of-concept scripts written by the security-engineer role while reviewing a pull request. They are **not CI tests**: no workflow runs them, they have no pass/fail exit code that CI reads, and they need a throw-away API and database that the person running them sets up. Each folder has its own README with the setup and run order. The written findings are in `docs/reviews/pr-<n>/` and `docs/security/`.

| Folder | Pull request | Topic |
|---|---|---|
| `public-id-retest` | PR-10 (public ids) | opaque ids, IDOR, cursors, uploads; report in `docs/security/2026-10-public-id-retest.md` |
| `pr106-roles-rp4b` | #106 (RP-4b) | roles as data, `allWarehouses`, last administrator, claims and refresh |
| `pr118-roles-rp5` | #118 (RP-5) | roles API: escalation, delete and in-use, validation, concurrency, audit |
| `pr124-rp6` | #124 (RP-6) | administrability by permission, lock-out matrix, races, two API instances |
| `pr134-assignable-roles` | #134 | roles assignable on the user form, information leak, users with no warehouse |
| `pl2-purchasing` | PL2.11 (pilot slice 2) | purchase orders, approval tiers, receive against a PO, reorder, accounting export, module switches; report in `docs/reviews/pl2-security-retest/report.md` |

How to run (all folders): Node 20+, no dependencies; publish the API in Release, start it on loopback against a new database with made-up seed passwords, set the environment variables named in the folder's README (`RV_BASE`, `RV_ADMIN_PW`, `RV_DEMO_PW`, `RV_OUT`), then run the scripts in the order the README gives, for example `node s0.mjs`, then `node p1.mjs`. Scratch output goes in a folder outside the repository; drop the database when done.

Rules: never point a script at a shared or production database; no real secret is stored in these folders (made-up throw-away passwords only); `pr106-roles-rp4b` has paths on the author's drive at the top of `lib.mjs` that you must adjust before reuse.
