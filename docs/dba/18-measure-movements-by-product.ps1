<#
  18-measure-movements-by-product.ps1 -- GET /api/reports/movements?productId=<id> through a running Ims.Api, the way a user triggers it: wall time of the
  call and the logical reads of the statements it ran (Query Store, so the page and both counts are included; the Query Store must be on, see 04-query-store-top.sql).

  Usage (API running against a perf database, see docs/dba/README.md section 9; it must be the only user of the database):
    powershell -File docs/dba/18-measure-movements-by-product.ps1 -Label after -BaseUrl http://localhost:5187 -Database Ims_Perf_Mov
        -Hot 3,4,5 -Cold 16961,43,20001
  Order matters while the statement has one cached plan: "hot-first" clears the plan cache, then asks for the hot products, then the cold ones;
  "cold-first" clears it and asks for the cold ones first. "default" is the report with no filter (page 1). Output: one line per call, and
  docs/dba/results-<Label>.csv. Credentials as in 02-measure-api.ps1 (Seed:AdminPassword of appsettings.Development.json).
#>
param(
    [string]$BaseUrl = 'http://localhost:5187',
    [string]$Label = 'movements-by-product',
    [string]$Database = 'Ims_Perf',
    [string]$Server = '(localdb)\ImsDev',
    [int[]]$Hot = @(3, 4, 5),
    [int[]]$Cold = @(16961, 43, 20001),
    [int]$Repeat = 3
)
if ($Database -notlike 'Ims_Perf*') { throw 'A perf database only.' }
$dev = Join-Path $PSScriptRoot '..\..\src\Ims.Api\appsettings.Development.json'
$password = (Get-Content $dev -Raw | ConvertFrom-Json).Seed.AdminPassword
$login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/auth/login" -ContentType 'application/json' -Body (@{ username = 'admin'; password = $password } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.accessToken)" }

function Invoke-Sql([string]$query) { sqlcmd -S $Server -E -d $Database -h -1 -W -Q "SET NOCOUNT ON; $query" }
function Get-Reads { [long](Invoke-Sql "EXEC sys.sp_query_store_flush_db; SELECT CAST(ISNULL(SUM(rs.avg_logical_io_reads * rs.count_executions), 0) AS bigint) FROM sys.query_store_runtime_stats rs JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id JOIN sys.query_store_query q ON q.query_id = p.query_id JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id WHERE qt.query_sql_text LIKE N'%StockDocumentLine%' AND qt.query_sql_text NOT LIKE N'%query_store%'") }

$rows = New-Object System.Collections.Generic.List[object]
function Measure-Call([string]$scenario, [string]$name, [string]$path) {
    $before = Get-Reads
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-RestMethod -Uri "$BaseUrl$path" -Headers $headers
    $sw.Stop()
    $reads = (Get-Reads) - $before
    $rows.Add([pscustomobject]@{ Label = $Label; Scenario = $scenario; Call = $name; Total = $r.total; Ms = $sw.ElapsedMilliseconds; Reads = $reads })
    '{0,-11} {1,-22} total {2,6}  {3,6} ms  {4,10} reads' -f $scenario, $name, $r.total, $sw.ElapsedMilliseconds, $reads
}

Invoke-RestMethod -Uri "$BaseUrl/api/reports/movements?productId=$($Cold[-1])" -Headers $headers | Out-Null   # warm-up: JIT and the connection pool, not timed
foreach ($scenario in 'hot-first', 'cold-first') {
    Invoke-Sql 'ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE' | Out-Null
    $order = if ($scenario -eq 'hot-first') { @($Hot) + @($Cold) } else { @($Cold) + @($Hot) }
    foreach ($id in $order) { Measure-Call $scenario "product $id" "/api/reports/movements?productId=$id" }
}
for ($i = 1; $i -le $Repeat; $i++) { Measure-Call 'default' "no filter #$i" '/api/reports/movements' }
$rows | Export-Csv (Join-Path $PSScriptRoot "results-$Label.csv") -NoTypeInformation

