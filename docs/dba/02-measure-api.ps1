<#
  02-measure-api.ps1 -- latency probe against a running Ims.Api (Windows PowerShell 5.1 / pwsh).

  Usage (API already running against the perf database, see docs/dba/README.md):
    powershell -File docs/dba/02-measure-api.ps1 -Label before -BaseUrl http://localhost:5187                        # 30 timed calls per endpoint
    powershell -File docs/dba/02-measure-api.ps1 -Label after  -BaseUrl http://localhost:5187 -Only 'ledger|documents'   # regex on endpoint name
  Credentials: -User / -Password, or the IMS_PERF_USER / IMS_PERF_PASSWORD environment variables; with neither, the admin user and
  Seed:AdminPassword of src/Ims.Api/appsettings.Development.json are used (the perf database is seeded by a Development start).

  One untimed warm-up call per endpoint is made first (plan compile, JIT), then -Runs timed calls on one keep-alive
  connection (-ExportRuns for the xlsx exports, which take seconds). p95 = nearest rank (29th of 30).
  Results go to docs/dba/results-<Label>.csv.  Deep-page positions are derived from the row totals the API reports, so the
  same script works at every volume tier.  Hot/mid product ids are read from the ledger through the API (/api/stock/ledger).
#>
param(
    [string]$BaseUrl = 'http://localhost:5109',
    [int]$Runs = 30,
    [int]$ExportRuns = 10,
    [string]$Label = 'baseline',
    [string]$Only = '.',
    [string]$User = $(if ($env:IMS_PERF_USER) { $env:IMS_PERF_USER } else { 'admin' }),
    [string]$Password = $env:IMS_PERF_PASSWORD,
    [int]$HotProduct = 3,                     # most ledger rows in the seed (Zipf head)
    [int]$MidProduct = 161,                   # ~25 ledger rows at the 100k tier
    [string]$ModernApi = 'auto'               # 'auto' | 'yes' | 'no': does the API support afterId/includeTotal/sortBy and paged low-stock?
)
if (-not $Password) {
    $dev = Join-Path $PSScriptRoot '..\..\src\Ims.Api\appsettings.Development.json'
    $Password = (Get-Content $dev -Raw | ConvertFrom-Json).Seed.AdminPassword
}
Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
$http = New-Object System.Net.Http.HttpClient($handler)
$http.Timeout = [TimeSpan]::FromSeconds(125)

$body = New-Object System.Net.Http.StringContent((@{ username = $User; password = $Password } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json')
$login = $http.PostAsync("$BaseUrl/api/auth/login", $body).GetAwaiter().GetResult()
$json = $login.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
if (-not $json.accessToken) { throw "login failed: $($login.StatusCode)" }
$http.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $json.accessToken)
function Do-Login { $b = New-Object System.Net.Http.StringContent((@{ username = $User; password = $Password } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json'); $l = $http.PostAsync("$BaseUrl/api/auth/login", $b).GetAwaiter().GetResult(); $j = $l.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json; $http.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $j.accessToken) }
function Get-Json([string]$path) { ($http.GetAsync($BaseUrl + $path).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult()) | ConvertFrom-Json }

$lt = Get-Json '/api/stock/ledger?pageSize=1'
$dt = Get-Json '/api/documents?pageSize=1'
$ledgerTotal = [int]$lt.total; $docTotal = [int]$dt.total
$modern = ($ModernApi -eq 'yes') -or ($ModernApi -eq 'auto' -and ($lt.PSObject.Properties.Name -contains 'nextCursor'))
$ledgerDeep = 20; $docDeep = 400     # the last OFFSET pages the API accepts (Limits.MaxOffsetRows = 10,000 rows): ledger page 20 x500, documents page 400 x25
"ledger rows $ledgerTotal, documents $docTotal, modern api: $modern, last OFFSET pages: ledger p$ledgerDeep (x500), docs p$docDeep (x25)"
# A cursor is opaque (encrypted), so it cannot be computed from an id: take the nextCursor of the deepest OFFSET page the API accepts (row 10,000; the seek that follows costs the same at any depth).
$ledgerCursor = [string](Get-Json "/api/stock/ledger?page=$ledgerDeep&pageSize=500&includeTotal=false").nextCursor; $docCursor = [string](Get-Json "/api/documents?page=$docDeep&pageSize=25&includeTotal=false").nextCursor
$d30 = (Get-Date).AddDays(-30).ToString('yyyy-MM-dd'); $d0 = (Get-Date).ToString('yyyy-MM-dd'); $d7 = (Get-Date).AddDays(-7).ToString('yyyy-MM-dd')

$e = [ordered]@{}
$e['stock: no filter (p1 x50)']              = '/api/stock'
$e['stock: warehouse=2']                     = '/api/stock?warehouseId=2'
$e['stock: warehouse=1 pageSize=500']        = '/api/stock?warehouseId=1&pageSize=500'
$e['stock: search=bolt (common)']            = '/api/stock?search=bolt'
$e['stock: search=SKU-00042 (sku)']          = '/api/stock?search=SKU-00042'
$e['stock: search=9557000000042 (barcode)']  = '/api/stock?search=9557000000042'
$e['stock: lowOnly']                         = '/api/stock?lowOnly=true'
$e['stock: lowOnly pageSize=500 p3']         = '/api/stock?lowOnly=true&pageSize=500&page=3'
$e['stock: category=7']                      = '/api/stock?categoryId=7'
$e['stock: product (hot)']                   = "/api/stock?productId=$HotProduct"
$e['stock: last OFFSET page 20 x500']           = '/api/stock?page=20&pageSize=500'
if ($modern) {
$e['stock: sort QtyOnHand desc']             = '/api/stock?sortBy=QtyOnHand&desc=true'
$e['stock: sort Value desc']                 = '/api/stock?sortBy=Value&desc=true'
$e['stock: sort Warehouse']                  = '/api/stock?sortBy=Warehouse'
$e['stock: sort Sku desc']                   = '/api/stock?sortBy=Sku&desc=true'
}
$e['ledger: product hot']                    = "/api/stock/ledger?productId=$HotProduct"
$e['ledger: product mid']                    = "/api/stock/ledger?productId=$MidProduct"
$e['ledger: product+warehouse']              = "/api/stock/ledger?productId=$HotProduct&warehouseId=1"
$e['ledger: no filter p1']                   = '/api/stock/ledger'
$e['ledger: date range 30d']                 = "/api/stock/ledger?from=$d30&to=$d0"
$e['ledger: date range 7d']                  = "/api/stock/ledger?from=$d7&to=$d0"
$e['ledger: warehouse=2']                    = '/api/stock/ledger?warehouseId=2'
$e['ledger: by source doc']                  = '/api/stock/ledger?sourceDocId=59'
$e['ledger: last OFFSET page (x500)']          = "/api/stock/ledger?page=$ledgerDeep&pageSize=500"
$e['ledger: OFFSET beyond the limit (400)']   = '/api/stock/ledger?page=1000&pageSize=500'
if ($modern) {
$e['ledger: deep cursor (after row 10,000, x500)'] = "/api/stock/ledger?cursor=$ledgerCursor&pageSize=500&includeTotal=false"
$e['ledger: p1 includeTotal=false']          = '/api/stock/ledger?includeTotal=false'
$e['ledger: hot product includeTotal=false'] = "/api/stock/ledger?productId=$HotProduct&includeTotal=false"
$e['ledger: 30d includeTotal=false']         = "/api/stock/ledger?from=$d30&to=$d0&includeTotal=false"
}
$e['stock/low-stock']                        = '/api/stock/low-stock'
$e['stock/summary']                          = '/api/stock/summary'
$e['documents: no filter']                   = '/api/documents'
$e['documents: type=2 status=3']             = '/api/documents?type=2&status=3'
$e['documents: status=0 (draft)']            = '/api/documents?status=0'
$e['documents: warehouse=2 type=1']          = '/api/documents?warehouseId=2&type=1'
$e['documents: date range 30d']              = "/api/documents?from=$d30&to=$d0"
$e['documents: search=RCV-WH02-2610']        = '/api/documents?search=RCV-WH02-2610'
$e['documents: search=0076 (middle)']        = '/api/documents?search=0076'
$e['documents: last OFFSET page (x25)']        = "/api/documents?page=$docDeep&pageSize=25"
if ($modern) {
$e['documents: deep cursor (after row 10,000, x25)'] = "/api/documents?cursor=$docCursor&pageSize=25&includeTotal=false"
$e['documents: p1 includeTotal=false']       = '/api/documents?includeTotal=false'
}
$e['documents/{id} (11 lines)']              = '/api/documents/59'
$e['documents/{id} (recent)']                = "/api/documents/$([int]$dt.items[0].id)"
$e['reports/movements: 7d']                  = "/api/reports/movements?from=$d7&to=$d0"
$e['reports/movements: 30d']                 = "/api/reports/movements?from=$d30&to=$d0"
$e['reports/movements: product hot']         = "/api/reports/movements?productId=$HotProduct"
$e['reports/movements: no filter']           = '/api/reports/movements'
$e['export: stock-summary xlsx']             = '/api/reports/stock-summary/export'
$e['export: movements xlsx (30d)']           = "/api/reports/movements/export?from=$d30&to=$d0"
$e['export: movements xlsx (no filter)']     = '/api/reports/movements/export'
$e['export: ledger xlsx (30d)']              = "/api/reports/ledger/export?from=$d30&to=$d0"
$e['export: ledger xlsx (no filter)']        = '/api/reports/ledger/export'
$e['export: low-stock xlsx']                 = '/api/reports/low-stock/export'
$e['products: no filter']                    = '/api/products'
$e['products: search=bolt']                  = '/api/products?search=bolt'
$e['products: search=SKU-00042']             = '/api/products?search=SKU-00042'
$e['products: search=9557000000042']         = '/api/products?search=9557000000042'
$e['products: search=zzzz (no match)']       = '/api/products?search=zzzz'
$e['products: deep page 90 x50']             = '/api/products?page=90&pageSize=50'
if ($modern) {
$e['products: sortBy=Name']                  = '/api/products?sortBy=Name'
$e['products: sortBy=Name desc p20']         = '/api/products?sortBy=Name&desc=true&page=20'
}
$e['products/lookup?code=SKU-00042']         = '/api/products/lookup?code=SKU-00042'
$e['audit: no filter']                       = '/api/audit'
$e['audit: entityType=StockDocument']        = '/api/audit?entityType=StockDocument'
$e['audit: entityType=User (rare)']             = '/api/audit?entityType=User'
$e['audit: actor=clerk (rare)']                 = '/api/audit?actor=clerk'
$e['audit: actor=supervisor (common)']          = '/api/audit?actor=supervisor'
if ($modern) { $e['audit: p1 includeTotal=false'] = '/api/audit?includeTotal=false' }
$e['users: list']                            = '/api/users'
$e['partners: search=Supplier 1']            = '/api/partners?search=Supplier 1'

function Get-Rank([double[]]$sorted, [double]$p) { $i = [math]::Ceiling($p * $sorted.Count) - 1; if ($i -lt 0) { $i = 0 }; $sorted[$i] }

$rows = @()
foreach ($name in $e.Keys) {
    if ($name -notmatch $Only) { continue }
    $url = $BaseUrl + [uri]::EscapeUriString($e[$name])
    $n = if ($name -like 'export:*') { $ExportRuns } else { $Runs }
    $times = New-Object System.Collections.Generic.List[double]
    $status = 0; $bytes = 0
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try { $r = $http.GetAsync($url).GetAwaiter().GetResult(); $b = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(); if ([int]$r.StatusCode -eq 401) { Do-Login; $r = $http.GetAsync($url).GetAwaiter().GetResult(); $b = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() } } catch { $status = -1 }
    $warm = $sw.Elapsed.TotalMilliseconds
    for ($i = 0; $i -lt $n; $i++) {
        $sw.Restart()
        try {
            $r = $http.GetAsync($url).GetAwaiter().GetResult()
            $b = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            if ([int]$r.StatusCode -eq 401) { Do-Login; $sw.Restart(); $r = $http.GetAsync($url).GetAwaiter().GetResult(); $b = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() }
            $status = [int]$r.StatusCode; $bytes = $b.Length
        } catch { $status = -1 }
        $sw.Stop()
        $times.Add($sw.Elapsed.TotalMilliseconds)
    }
    $s = [double[]]($times | Sort-Object)
    $row = [pscustomobject]@{
        Label = $Label; Endpoint = $name; Url = $e[$name]; Runs = $n; Status = $status; Bytes = $bytes
        WarmMs = [math]::Round($warm, 1); P50 = [math]::Round((Get-Rank $s 0.5), 1); P95 = [math]::Round((Get-Rank $s 0.95), 1)
        MaxMs = [math]::Round($s[-1], 1); MeanMs = [math]::Round(($s | Measure-Object -Average).Average, 1)
    }
    $rows += $row
    '{0,-42} {1} {2,9} B  warm {3,8} p50 {4,8} p95 {5,8} max {6,8}' -f $name, $status, $bytes, $row.WarmMs, $row.P50, $row.P95, $row.MaxMs
}
$out = Join-Path $PSScriptRoot "results-$Label.csv"
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $out
"saved $out"
