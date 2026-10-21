<#
  25-measure-reorder.ps1 -- GET /api/purchase-orders/suggestions through a running Ims.Api (PL2.6, README section 13), for every active warehouse of the perf database:
    page1   the first page (pageSize 50) of the warehouse, the count and the page
    deep    page 20 of the same list (offset 950)
    search  a search on the SKU prefix of the first row
  The database must have the purchasing module on and some open order lines (README section 13 says how the copy was prepared).
  Usage (API running against a RESTORED COPY named Ims_Perf*, never the original):
    powershell -File docs/dba/25-measure-reorder.ps1 -Label 1m-reorder -BaseUrl http://localhost:5831 -Database Ims_PerfReorderCopy
  Output: a summary per kind (avg, P50, P95, max in ms) and docs/dba/results-<Label>.csv. Credentials as in 02-measure-api.ps1.
#>
param(
    [string]$BaseUrl = 'http://localhost:5831',
    [string]$Label = 'reorder',
    [Parameter(Mandatory)][string]$Database,
    [int]$Count = 20
)
if ($Database -notlike 'Ims_Perf*') { throw 'A perf database only.' }
$dev = Join-Path $PSScriptRoot '..\..\src\Ims.Api\appsettings.Development.json'
$password = (Get-Content $dev -Raw | ConvertFrom-Json).Seed.AdminPassword
$login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/auth/login" -ContentType 'application/json' -Body (@{ username = 'admin'; password = $password } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.accessToken)" }

$all = Invoke-RestMethod -Uri "$BaseUrl/api/warehouses?pageSize=50" -Headers $headers
$warehouses = @($all | ForEach-Object { $_ } | Where-Object { $_.isActive })
$rows = New-Object System.Collections.Generic.List[object]
function Measure-Call([string]$kind, [string]$warehouse, [string]$query) {
    $url = "$BaseUrl/api/purchase-orders/suggestions?warehouseId=$warehouse$query"
    Invoke-RestMethod -Uri $url -Headers $headers | Out-Null   # untimed: plan compile, connection
    for ($i = 0; $i -lt $Count; $i++) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $r = Invoke-RestMethod -Uri $url -Headers $headers
        $sw.Stop()
        $rows.Add([pscustomobject]@{ Label = $Label; Kind = $kind; Warehouse = $warehouse; Total = $r.total; Items = @($r.items).Count; Ms = $sw.ElapsedMilliseconds })
    }
}
foreach ($w in $warehouses) {
    $first = (Invoke-RestMethod -Uri "$BaseUrl/api/purchase-orders/suggestions?warehouseId=$($w.id)&pageSize=50" -Headers $headers)
    Measure-Call 'page1' $w.id '&pageSize=50'
    Measure-Call 'deep' $w.id '&pageSize=50&page=20'
    if (@($first.items).Count -gt 0) {
        $prefix = ([string]$first.items[0].sku).Substring(0, [math]::Min(5, ([string]$first.items[0].sku).Length))
        Measure-Call 'search' $w.id "&pageSize=50&search=$([uri]::EscapeDataString($prefix))"
    }
}
foreach ($kind in 'page1', 'deep', 'search') {
    $ms = @($rows | Where-Object { $_.Kind -eq $kind } | ForEach-Object { $_.Ms } | Sort-Object)
    if ($ms.Count -eq 0) { continue }
    '{0,-7} n {1,3}  avg {2,6:N0} ms  P50 {3,5} ms  P95 {4,5} ms  max {5,5} ms' -f $kind, $ms.Count, ($ms | Measure-Object -Average).Average, $ms[[int][math]::Floor(($ms.Count - 1) * 0.5)], $ms[[int][math]::Ceiling($ms.Count * 0.95) - 1], $ms[-1]
}
$rows | Where-Object { $_.Kind -eq 'page1' } | Group-Object Warehouse | ForEach-Object { 'warehouse {0}: {1} rows to order' -f $_.Name, ($_.Group | Select-Object -First 1).Total }
$out = Join-Path $PSScriptRoot "results-$Label.csv"
$rows | Export-Csv -Path $out -NoTypeInformation
"written $out"
