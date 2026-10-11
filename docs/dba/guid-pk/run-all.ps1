<#
  run-all.ps1 -- runs run-variant.ps1 for the six strategies one after the other (never in parallel: the measurements need a quiet machine).
  Baseline first. About 1 hour per variant on a 4-core Express engine.   powershell -File run-all.ps1
#>
param([string[]]$Variants = @('C', 'A1', 'A3', 'A2', 'B1', 'B2'), [string]$DataDir = (Join-Path $env:TEMP 'ims_guidpk'))
foreach ($v in $Variants) {
    & (Join-Path $PSScriptRoot 'run-variant.ps1') -Variant $v -DataDir $DataDir
}
