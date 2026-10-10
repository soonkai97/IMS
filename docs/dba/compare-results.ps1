<# compare-results.ps1 -Before results-x.csv -After results-y.csv : p50/p95 side by side as markdown table rows #>
param([string]$Before, [string]$After, [double]$ThresholdMs = 1000)
$rowsBefore = Import-Csv $Before
$afterByName = @{}
foreach ($x in (Import-Csv $After)) { $afterByName[$x.Endpoint] = $x }
foreach ($r in $rowsBefore) {
    $o = $afterByName[$r.Endpoint]; if (-not $o) { continue }
    $flag = if ([double]$o.P95 -gt $ThresholdMs) { ' **>1s**' } else { '' }
    '| {0} | {1} | {2} | {3} | {4}{5} |' -f $r.Endpoint, $r.P50, $r.P95, $o.P50, $o.P95, $flag
}
