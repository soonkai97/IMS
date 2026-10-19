# PR 118 (RP-5, roles API) probes

Node 20 scripts, no dependencies. They drive a locally running Release API and print `ok` / `FAIL` per expectation (a `FAIL` on a name-shape line that says "(expected ...)" is an expectation of the script, read the line).

Setup: publish the PR head in Release, create a throw-away database on `(localdb)\ImsDev` with its files on D:, start the API on loopback with `ConnectionStrings__Default`, `Seed__AdminPassword`, `Seed__DemoPassword` (made-up values) and `Jwt__AccessMinutes=30`. Then export `RV_BASE` (default `http://127.0.0.1:5318`), `RV_ADMIN_PW`, `RV_DEMO_PW` (the two seed passwords you started the API with) and `RV_OUT` (a scratch folder on D:).

Order: `s0` (custom roles and users), `tokens` (re-issue tokens when they expire), then
`p1` escalation through role writes, `p2` delete / in use / recreate / races with the user save, `p3` catalog and text validation, `p4` endpoint matrix, ids, mass assignment, concurrency, audit, `p5` role soft-deleted or inactive by a database edit (uses `sqlcmd`, `RV_DB`), `p6` staleness, headers, CORS, `p7` scoped delegate against a wide-scope holder, `p8` all-permissions custom role against the system roles, `race` delete-vs-create-user race (run it against a build without `LockRolesAsync` in `DeleteRoleHandler` to see the lock matter), `audit-q` audit rows.

No secret is stored. The users the scripts create use one made-up throw-away password.
