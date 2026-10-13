<#
  24-measure-product-lookup.ps1 -- GET /api/products/lookup?code=<code> through a running Ims.Api (the document line picker and scanning), for codes spread over
  the whole catalog. The old statement (Sku = code OR a barcode = code, TOP 1) walked the Product table until the first match, so the cost grew with the position
  of the product (README section 4d). Codes spread evenly over the catalog by product id (the first, the last and everything between), so two runs ask for the same codes:
    sku      -Count SKUs (found)
    barcode  -Count barcodes (found)
    sku-lc   the same SKUs in lower case (found: the comparison is case-insensitive)
    unknown  -Count codes nobody owns (found = false)
  Usage (API running against a perf database, see docs/dba/README.md section 9):
    powershell -File docs/dba/24-measure-product-lookup.ps1 -Label 1m-lookup-after -BaseUrl http://localhost:5187 -Database Ims_Perf_Docs
  Output: a summary per kind (avg, P50, P95, max in ms) and docs/dba/results-<Label>.csv. Credentials as in 02-measure-api.ps1.
#>
param(
    [string]$BaseUrl = 'http://localhost:5187',
    [string]$Label = 'product-lookup',
    [string]$Database = 'Ims_Perf',
    [string]$Server = '(localdb)\ImsDev',
    [int]$Count = 40
)
if ($Database -notlike 'Ims_Perf*') { throw 'A perf database only.' }
$dev = Join-Path $PSScriptRoot '..\..\src\Ims.Api\appsettings.Development.json'
$password = (Get-Content $dev -Raw | ConvertFrom-Json).Seed.AdminPassword
$login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/auth/login" -ContentType 'application/json' -Body (@{ username = 'admin'; password = $password } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.accessToken)" }

function Invoke-Sql([string]$query) { sqlcmd -S $Server -E -d $Database -h -1 -W -Q "SET NOCOUNT ON; $query" }
$total = [int](Invoke-Sql 'SELECT COUNT(*) FROM Product WHERE REC_DELETED = 0')
$ids = (0..($Count - 1) | ForEach-Object { 1 + [int][math]::Floor(($_ + 0.5) * $total / $Count) }) -join ','   # evenly spread over the catalog: early and late products
$skus = @(Invoke-Sql "SELECT Sku FROM Product WHERE Id IN ($ids) ORDER BY Id")
$barcodes = @(Invoke-Sql "SELECT MIN(Barcode) FROM ProductBarcode WHERE ProductId IN ($ids) GROUP BY ProductId ORDER BY ProductId")
$unknown = 1..$Count | ForEach-Object { 'NO-SUCH-CODE-{0:D6}' -f $_ }
$kinds = [ordered]@{ 'sku' = $skus; 'barcode' = $barcodes; 'sku-lc' = @($skus | ForEach-Object { $_.ToLowerInvariant() }); 'unknown' = $unknown }

Invoke-RestMethod -Uri "$BaseUrl/api/products/lookup?code=$($skus[0])" -Headers $headers | Out-Null   # warm-up: JIT, connection
$rows = New-Object System.Collections.Generic.List[object]
foreach ($kind in $kinds.Keys) {
    foreach ($code in $kinds[$kind]) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $r = Invoke-RestMethod -Uri "$BaseUrl/api/products/lookup?code=$([uri]::EscapeDataString($code))" -Headers $headers
        $sw.Stop()
        $rows.Add([pscustomobject]@{ Label = $Label; Kind = $kind; Code = $code; Found = [bool]$r.found; ProductId = $r.product.id; Ms = $sw.ElapsedMilliseconds })
    }
}
foreach ($kind in $kinds.Keys) {
    $ms = @($rows | Where-Object { $_.Kind -eq $kind } | ForEach-Object { $_.Ms } | Sort-Object)
    $found = @($rows | Where-Object { $_.Kind -eq $kind -and $_.Found }).Count
    '{0,-8} n {1,3}  found {2,3}  avg {3,6:N0} ms  P50 {4,5} ms  P95 {5,5} ms  max {6,5} ms' -f $kind, $ms.Count, $found, ($ms | Measure-Object -Average).Average, $ms[[int][math]::Floor(($ms.Count - 1) * 0.5)], $ms[[int][math]::Ceiling($ms.Count * 0.95) - 1], $ms[-1]
}
$out = Join-Path $PSScriptRoot "results-$Label.csv"
$rows | Export-Csv -Path $out -NoTypeInformation
"written $out"
