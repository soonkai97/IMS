<#
  run-all.ps1 -Tier t1m [-SoakDuration 10m] [-BaseUrl http://localhost:5109]
  Runs every scenario in sequence against the running API and records SQL/process evidence for each (see run-scenario.ps1).
  Total time with defaults: about 26 minutes (+ soak). Run from the repository root.
#>
param([Parameter(Mandatory)] [string]$Tier, [string]$SoakDuration = '10m', [string]$BaseUrl = 'http://localhost:5109')
$plan = @(
    @{ s = 'post-burst'; vus = $null; d = $null },
    @{ s = 'read-mix';   vus = '50';  d = '2m' },
    @{ s = 'write-mix';  vus = '20';  d = '2m' },
    @{ s = 'write-hot';  vus = '30';  d = '2m' },
    @{ s = 'mixed';      vus = '50';  d = '3m' },
    @{ s = 'ramp';       vus = $null; d = $null },
    @{ s = 'spike';      vus = $null; d = $null },
    @{ s = 'soak';       vus = '50';  d = $SoakDuration }
)
foreach ($p in $plan) {
    $args = @{ Scenario = $p.s; Tier = $Tier; BaseUrl = $BaseUrl }
    if ($p.vus) { $args.Vus = $p.vus }
    if ($p.d) { $args.Duration = $p.d }
    Write-Host "=== $($p.s) ($Tier) $(Get-Date -Format HH:mm:ss)"
    & (Join-Path $PSScriptRoot 'run-scenario.ps1') @args
    Start-Sleep 20
}
Write-Host "=== all done $(Get-Date -Format HH:mm:ss)"
