# E2E health pass and a proposal for a PR gate

Written by the behavior-test-engineer role, 2026-10-19. Proposal only: no workflow is changed here.

## Findings of the full run (one throw-away database `Ims_E2E`, production web build, real API)

131 scenarios in 26 files. Before the fixes: 130 passed, 1 failed on a fresh database; the run took 20.4 minutes (22 + 30 + 42 + 37 scenarios in four chunks).

| Spec | Class | Cause | Fix |
|---|---|---|---|
| `17-feedback` sign-in 500 | test defect | counted the server's sentence, which the page never shows (the page has its own sentence, LG-18); fails on main since that rule | counts the page's own sentence (#128) |
| `15-containment` large receipt: "requires approval" | isolation (approval rule left in the shared database) | `api.post` assumed no rule covers a receipt; a rule in Ims_Dev did | `api.post` approves a document a rule holds back (#128); checked by adding a Receipt rule to the database |
| `15-containment` toast test times out | test defect (race) | the dialog takes its errors with it when it closes (by design), so `Dismiss` is clicked on a toast that is leaving: "element was detached, retrying" until the 300 s timeout. Reproduced on a fresh database in the final full run (not in the first). Not the rate limit | waits for the toast to be hidden instead (#128) |
| `15-containment` 300-character names, "Show more" by keyboard | test defect (race) | the list redraws between `focus()` and the Enter key, the key is lost; 1 run in 4 | focus, key and assertion retried as one unit (#128); 10 of 10 runs pass |
| all long runs | isolation (rate limit) | every scenario signed in through the form and the pacing wait for the 20-a-minute limit counted against the 60 s test timeout | kept sessions: a run goes from 20 to 5-7 minutes (#128) |
| `15-containment` Add item sheet | product bug | a 64-character SKU is one unbroken run: right edge at 500 px of 360, name 0 px wide | #129 (one SCSS rule + a check) |
| `15-containment` pinned column | test defect (borderline layout) | the 4th row's button sat below the 700 px window edge, `elementFromPoint` returned null; failed 4 of 4 on a database with other data | scrolls the button on screen first (#128) |
| `16-shell` reduced motion bar | test defect (timing) | sampled the first frame of a 0.01 ms animation | polls until the bar rests (#128) |
| `08-session` return URL | test defect (introduced by kept sessions, caught on the second run) | a restored session lands on Home, only the form honours the return URL | signs in through the form on purpose (#128) |
| all | isolation | shared mutable state (negative stock, approval rule, data without cleanup) | already restored by their own `finally`; documented; second and third runs on a used database pass |

After the fixes: 131 of 131 on a used database (two runs); on a fresh database 129 of 131, the two failures being the races above, fixed afterwards and run 10 times (5 each) without a failure. Full run about 5 to 7 minutes (machine load varies), measured from the start of the first chunk to the end of the last.

No larger product bug was found.

## Proposal: which specs gate a pull request

Measured time per file (seconds, test time only, kept sessions): 01 9, 02 2, 03 6, 04 4, 05 2, 06 7, 07 2, 08 3, 09 3, 10 13, 11 45, 12 1, 13 7, 14 4, 15-button 1, 15-containment 54, 16 10, 17-feedback 6, 17-opaque 3, 18 67, 25 7, 26 6, 30 13, 31 5, 32-overlays 3, 32-roles 4. Everything together is about 5 minutes of test time.

**Smoke set (required check on every pull request that touches `web/`, `e2e/` or `src/`)**: `01-receive`, `03-approval-override`, `04-reversal`, `06-roles`, `10-phone`, `12-low-stock`, `13-cost-visibility`, `15-containment`, `17-feedback`, `31-table-toolbar`. These are the money, posting, reversal, permission and cost-visibility flows plus the layout checks that regressed behind a green CI (#121). About 2.5 minutes of tests.

**Nightly (and on demand)**: the whole suite (all 26 files) on a fresh database, with a notification on failure. About 5 minutes of tests.

The whole suite is short enough that gating on all of it is also possible; the smoke set is the safer first step because every flake in a required check blocks every PR.

### Workflow shape (not applied)

```yaml
# .github/workflows/e2e.yml, replacing the manual-only trigger
on:
  pull_request:          # smoke
    paths: ["web/**", "e2e/**", "src/**", ".github/workflows/e2e.yml"]
  schedule:
    - cron: "17 3 * * *" # nightly, full
  workflow_dispatch:
# steps as today (SQL Server service container, API build output, web production build on :4200, Chromium), then:
#   run: npx playwright test ${{ github.event_name == 'pull_request' && 'tests/01* tests/03* tests/04* tests/06* tests/10* tests/12* tests/13* tests/15-containment* tests/17-feedback* tests/31*' || '' }}
# retries: 1 on CI (already in playwright.config.ts); upload report and traces on failure (already there).
```

Make the job a required status check in the branch protection (job name stays stable). The smoke job needs the API and web builds, so it costs about 4 to 6 minutes of setup on top of 2.5 minutes of tests: target under 10 minutes wall time, so the "under 8 minutes" target needs the build caches (nuget and npm caches are already used by `ci.yml`) and building the API and the web app in parallel steps or reusing the `ci.yml` build artifacts. Not measured on a runner yet: run it once with `workflow_dispatch` before making it required.

### Cost

The repository is public: GitHub Actions minutes on standard hosted runners are free, so the $0 stop-usage budget is not touched. If it were private (Free plan, 2,000 minutes a month): smoke about 9 minutes x 60 pull requests a month = 540 minutes, nightly full about 11 minutes x 30 = 330 minutes, about 870 minutes in total.

### Flake risk

- Medium to low for the smoke set. The remaining risks are timing checks on animations (`16-shell`, `32-overlays`; not in the smoke set) and layouts at the edge of a window (the pinned column one, fixed). `15-containment` is the largest file (13 scenarios, about a minute); it failed once under `--repeat-each` with a "Show more" keyboard step that passed in five later runs and was not reproduced: watch it first.
- Each flake in a required check blocks merges: keep `retries: 1` on CI, report flaky scenarios, and move a scenario out of the smoke set after two flakes in a month.
- The sign-in limit (20 a minute per address) is not a risk on a runner with kept sessions: the form is used mostly by the login scenarios and once per role (the limiter log paces anything beyond 15 a minute).
- A pull request run uses a fresh database every time, so no leftover rule or setting can reach it. The shared `Ims_Dev` should never be the target of the suite.
