<#
  run-scenario.ps1 -Scenario read-mix -Tier t1m [-Vus 50] [-Duration 2m]
  Runs one k6 scenario against an already running API (BASE_URL) and records, around it:
    - SQL Server counters before/after (deadlock graphs + victims, lock waits) + top waits            -> tests/load/results/<tier>/<scenario>-sql.txt
    - CPU % and working set of the API process and of sqlservr every 5 s                              -> tests/load/results/<tier>/<scenario>-proc.csv
    - the k6 summary JSON written by scenarios.js                                                    -> tests/load/results/<tier>/<scenario>.json
  Run from the repository root. Requires k6 on PATH and sqlcmd; assumes the LocalDB instance (localdb)\ImsDev against a throw-away perf database (never a database with real data).
#>
param(
    [Parameter(Mandatory)] [string]$Scenario,
    [Parameter(Mandatory)] [string]$Tier,
    [string]$Vus, [string]$Duration,
    [string]$BaseUrl = 'http://localhost:5109',
    [string]$Server = '(localdb)\ImsDev'
)
$ErrorActionPreference = 'Continue'
$root = (Get-Location).Path
$results = Join-Path $root "tests/load/results/$Tier"; New-Item -ItemType Directory -Force $results | Out-Null
$tag = "$Tier-$Scenario"

sqlcmd -S $Server -E -d tempdb -b -W -v Label="before_$tag" -i (Join-Path $root 'docs/dba/11-snapshot.sql') | Out-Null

# resource sampler (runs as a background job)
$api = Get-NetTCPConnection -LocalPort ([uri]$BaseUrl).Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
$apiPid = if ($api) { $api.OwningProcess } else { 0 }
$sampler = Start-Job -ArgumentList $apiPid, (Join-Path $results "$Scenario-proc.csv") -ScriptBlock {
    param($apiPid, $csv)
    'time,proc,cpu_pct,ws_mb' | Out-File $csv -Encoding ascii
    $cores = [Environment]::ProcessorCount
    $prev = @{}
    while ($true) {
        foreach ($p in @(@{ n = 'api'; id = $apiPid }) + @(Get-Process sqlservr -ErrorAction SilentlyContinue | ForEach-Object { @{ n = 'sqlservr'; id = $_.Id } })) {
            $proc = Get-Process -Id $p.id -ErrorAction SilentlyContinue; if (-not $proc) { continue }
            $now = Get-Date; $cpu = $proc.TotalProcessorTime.TotalSeconds
            if ($prev.ContainsKey($p.id)) {
                $dt = ($now - $prev[$p.id].t).TotalSeconds
                if ($dt -gt 0) { $pct = [math]::Round(($cpu - $prev[$p.id].c) / $dt / $cores * 100, 1); ('{0},{1},{2},{3}' -f $now.ToString('HH:mm:ss'), $p.n, $pct, [math]::Round($proc.WorkingSet64 / 1MB)) | Out-File $csv -Append -Encoding ascii }
            }
            $prev[$p.id] = @{ t = $now; c = $cpu }
        }
        Start-Sleep 5
    }
}

$env:BASE_URL = $BaseUrl; $env:OUT_DIR = "tests/load/results/$Tier"
if ($Vus) { $env:VUS = $Vus } else { Remove-Item Env:VUS -ErrorAction SilentlyContinue }
if ($Duration) { $env:DURATION = $Duration } else { Remove-Item Env:DURATION -ErrorAction SilentlyContinue }
k6 run --quiet (Join-Path $root "tests/load/$Scenario.js") 2>&1 | Tee-Object -FilePath (Join-Path $results "$Scenario-k6.txt")

Stop-Job $sampler; Remove-Job $sampler -Force
sqlcmd -S $Server -E -d tempdb -b -W -v Label="after_$tag" -i (Join-Path $root 'docs/dba/11-snapshot.sql') | Out-Null
sqlcmd -S $Server -E -d tempdb -W -v A="before_$tag" B="after_$tag" -i (Join-Path $root 'docs/dba/11b-snapshot-diff.sql') | Tee-Object -FilePath (Join-Path $results "$Scenario-sql.txt")
