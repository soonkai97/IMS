# DAST: weekly OWASP ZAP scan of our own API

Workflow: `.github/workflows/dast.yml`. Accepted findings: `.github/zap/rules.tsv`. It runs only on a weekly schedule (Tuesday 02:41 UTC) and by hand
(Actions, DAST, Run workflow). It never runs on pull requests: an active scan sends thousands of hostile requests and takes up to an hour.

## 1. What it does

1. Starts a throw-away SQL Server container (a job service), builds the API and starts it from the build output on `http://localhost:5199` inside the runner (Development environment: migrations and demo accounts on start-up, OpenAPI document at `/openapi/v1.json`). Credentials are CI-only values defined in the workflow; nothing outside the runner is touched.
2. Signs in as the demo admin with `curl`, masks the access token in the log and gives it to ZAP.
3. Runs the **ZAP API scan** (`zap-api-scan.py`, image `ghcr.io/zaproxy/zaproxy:2.17.0` pinned by digest) with `--network host` against the live OpenAPI document. ZAP imports every operation, runs the passive rules and an **active scan** of each one, with the bearer token added to every request by a small hook (`zap.replacer`). Authenticated scanning means ZAP reaches the endpoints behind authorization, including the writing ones; the data is disposable.
4. Uploads `report.html`, `report.md`, `report.json` and the API log as the artifact `zap-api-scan` (30 days).
5. **Fails the run only on high-risk alerts** (ZAP risk code 3) that are not accepted in `rules.tsv`. Medium, low and informational alerts are in the report and counted in the job log, never a failure. A ZAP error (exit code 3 or more, no report) also fails.

Scan scope is our own API inside the runner and nothing else. The scan is deliberately not pointed at any deployed environment.

## 2. Settings that differ from production (so read the report with them in mind)

| Setting | In the scan | Why |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` | The OpenAPI document and demo accounts exist only there. Consequence: no HSTS header, cookies without `Secure` over plain http. Check those two with the production header check in `web-headers.md` section 6 instead. |
| `RateLimit__PerMinute` | `1000000` | One address with one token would hit the 600-per-minute limit within seconds and the rest of the scan would only see 429. |
| `Jwt__AccessMinutes` | `180` | The token must outlive the scan (default 5 minutes). Scan-only: the freshness figures of the roles ADR (a demotion applies within about 5.5 minutes, 5 plus the 30 s skew) hold only for the default; a larger value widens that window by the same amount, so never set it outside a scan or load run. |
| Sign-in limit | unchanged (20 per minute per address) | The login and refresh endpoints are scanned at that pace and mostly answer 429; that is the control working. |

## 3. Reading the result

- Job log: the lines `HIGH  <rule id>  <name>` and a count per risk level.
- Artifact `zap-api-scan`: `report.html` for people, `report.json` for tools. Each alert lists the URL, method, parameter, attack and evidence.
- A weekly red run means a new high-risk alert or an infrastructure problem (SQL container, ZAP image pull). Look at the failing step.

## 4. Accepting a finding (false positive or a risk the owner accepts)

Edit `.github/zap/rules.tsv`, one line per rule, tab separated:

```
<ZAP rule id>	IGNORE	<why this is accepted>
```

Rules: a reason is mandatory; the pull request that adds the line is the approval (a security reviewer must approve it); revisit when the code changes.
`IGNORE` removes the rule from the summary and from the high-risk gate. Do not use it to silence a real finding. Rule ids are listed at <https://www.zaproxy.org/docs/alerts/>.
The file starts with three informational rules (storable content, missing Sec-Fetch headers, user agent fuzzer) that are inherent to a JSON API.

## 5. Running it locally

Needs Docker and a SQL Server (the LocalDB instance `(localdb)\ImsDev` is not reachable from a container, so use the compose database or a throw-away container).

```bash
# 1. API on a free port, Development, throw-away database
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 RateLimit__PerMinute=1000000 Jwt__AccessMinutes=180 \
  ConnectionStrings__Default="Server=localhost,1433;Database=Ims_Dast;User Id=sa;Password=<local sa password>;TrustServerCertificate=True" \
  dotnet run --project src/Ims.Api -c Release
# 2. token + ZAP as in the workflow steps "Sign in for the scan" to "ZAP API scan" (copy them; on Docker Desktop for Windows/macOS
#    use --network host only with host networking enabled, or replace localhost with host.docker.internal in -t)
```

Drop the database afterwards. Never point ZAP at a host you do not own.

## 6. Maintenance

- ZAP version: bump `ZAP_IMAGE` (tag and digest from `docker buildx imagetools inspect ghcr.io/zaproxy/zaproxy:<tag>`) every quarter, together with a look at new alert ids.
- Actions are pinned by commit SHA (checkout v7, setup-dotnet v6, upload-artifact v7); Dependabot's actions ecosystem keeps them fresh.
- Cost: about 20 to 45 minutes of runner time per week.
- Related: CodeQL (static), `dotnet list package --vulnerable` and `npm audit` (dependencies) run in `ci.yml` and `codeql.yml`; DAST is the runtime counterpart.
