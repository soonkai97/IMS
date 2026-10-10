# Load tests (k6)

k6 scenarios for the API at volume. They drive the real HTTP pipeline, so run them against a **throw-away perf database** seeded with
`docs/dba/01-seed-perf.sql` (they create documents and post stock; never point them at a database with real data). The measured results
and the DBA conclusions are in `docs/dba/README.md`.

## Prerequisites

- [k6](https://k6.io/docs/get-started/installation/) on the PATH (`k6 version`), and `sqlcmd` for `run-scenario.ps1`.
- A perf database whose name starts with `Ims_Perf`, migrated and seeded (steps 1-4 of "Re-run the measurements" in `docs/dba/README.md`).
  The scenarios expect what that seed creates: the five demo users (`admin`, `owner`, `supervisor`, `staff`, `clerk`), 3-5 warehouses
  (`WAREHOUSES`, default 5), products `SKU-00001...` with the hot products first.
- The demo passwords are read from `Seed:AdminPassword` / `Seed:DemoPassword` in `src/Ims.Api/appsettings.Development.json`
  (override with the `ADMIN_PASSWORD` / `DEMO_PASSWORD` environment variables).

## Start a local API on the perf database

The default rate limit (600 requests per minute per user) would turn a load test into a rate-limit test, so raise it for the run.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5109'
$env:RateLimit__PerMinute = '1000000'
$env:ConnectionStrings__Default = 'Server=(localdb)\ImsDev;Database=Ims_Perf;Trusted_Connection=True;TrustServerCertificate=True'
dotnet run --project src/Ims.Api -c Release --no-launch-profile
```

Stop it when you are done (`Ctrl+C`). Do not run it while `dotnet build` / `dotnet test` need the same output folder (locked DLLs).

## Run one scenario

```powershell
# from the repository root; BASE_URL defaults to http://localhost:5109
k6 run -e BASE_URL=http://localhost:5109 -e VUS=10 -e DURATION=60s tests/load/read-mix.js
```

The end-of-run summary is printed and also written to `tests/load/results/<scenario>.json` (git-ignored; change the folder with `-e OUT_DIR=...`).
A threshold that is crossed makes k6 exit non-zero and print `thresholds on metrics ... have been crossed`.

| Script | What it does | Environment (defaults) |
|---|---|---|
| `read-mix.js` | stock enquiries, ledger by product, document lists, product search, low stock, summary | `VUS` 50, `DURATION` 2m |
| `write-mix.js` | create receipt/issue, submit, approve (about 5 %), post; spread over warehouses and products | `VUS` 20, `DURATION` 2m |
| `write-hot.js` | the same flow on 5 hot products in one warehouse (balance row and document-number contention) | `VUS` 30, `DURATION` 2m |
| `mixed.js` | 80 % reads, 20 % write flows | `VUS` 50, `DURATION` 3m |
| `post-burst.js` | 50 VUs post on the same 5 hot products at the same moment | `ITERATIONS` 3 |
| `ramp.js` / `spike.js` | 10 to 200 VUs over about 5.5 minutes / jump to 300 VUs and recover | none |
| `soak.js` | 50 VUs, mixed, 10 minutes (tokens last 15 minutes, keep `DURATION` at 12m or less) | `VUS` 50, `DURATION` 10m |

Other knobs: `WAREHOUSES` (5), `HOT_PRODUCTS` (5), `DEBUG=1` prints the first failing responses.
Thresholds (all scenarios): read P95 < 1 s, write P95 < 2 s, failed requests < 1 %, and no 5xx, 504 or 409.
Login is rate limited, so each scenario logs in once in `setup()` and shares the tokens; access tokens expire after 15 minutes (401s are counted).

## Run with SQL Server evidence

`run-scenario.ps1` wraps k6 and records, per scenario, the deadlock and lock-wait counters before/after, the top waits, and the CPU/memory of
the API and `sqlservr` every 5 seconds, into `tests/load/results/<tier>/` (git-ignored):

```powershell
powershell -File tests/load/run-scenario.ps1 -Scenario read-mix -Tier 1m -Vus 20 -Duration 2m -BaseUrl http://localhost:5109
powershell -File tests/load/run-all.ps1 -Tier 1m          # every scenario in sequence, about 26 minutes plus the soak
```

It uses `docs/dba/11-snapshot.sql` and `11b-snapshot-diff.sql` (LocalDB exposes no lock performance counters, so deadlocks are read from the
`system_health` session and the exception ring buffer).

## Reading the numbers

The closed-loop scenarios (a VU sends the next request as soon as the last one returns) measure capacity, not user experience: the
`read-mix` saturates at roughly 45 requests/s on the reference machine because SQL Server Express uses at most 4 cores. Compare runs on
the same machine, with the same database and VU count, and repeat a run before believing a difference smaller than about 30 %.
