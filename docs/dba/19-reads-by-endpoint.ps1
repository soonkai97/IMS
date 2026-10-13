<#
  19-reads-by-endpoint.ps1 -- logical reads per HTTP call, for the endpoints of a 02-measure-api.ps1 result file.

  02-measure-api.ps1 records wall time only. This script takes the URLs it recorded (column Url, with the public ids and cursors it looked up), calls each one
  again through the running Ims.Api and reads the statement-level logical reads from the Query Store around the calls, so the number is what the whole
  request cost in SQL Server (the page, the count, the permission and scope look-ups, the audit insert of a login is excluded because the token is reused).

  Usage (API running against the perf database, it must be the only user of the database; Query Store ON with QUERY_CAPTURE_MODE = ALL, see 04-query-store-top.sql):
    powershell -File docs/dba/19-reads-by-endpoint.ps1 -From docs/dba/results-1m-public-ids.csv -Label 1m-public-ids -BaseUrl http://localhost:5611 -Database Ims_Perf_X
  One untimed call per endpoint first (plan compile), then -Calls timed calls (-ExportCalls for the xlsx exports); reads per call = delta of
  SUM(avg_logical_io_reads * count_executions) over all Query Store statements / number of calls. Output: docs/dba/results-<Label>-reads.csv.
  Credentials as in 02-measure-api.ps1.
#>
param(
    [Parameter(Mandatory)][string]$From,
    [string]$BaseUrl = 'http://localhost:5109',
    [Parameter(Mandatory)][string]$Database,
    [string]$Server = '(localdb)\ImsDev',
    [string]$Label = 'reads',
    [string]$Only = '.',
    [int]$Calls = 5,
    [int]$ExportCalls = 2,
    [string]$User = $(if ($env:IMS_PERF_USER) { $env:IMS_PERF_USER } else { 'admin' }),
    [string]$Password = $env:IMS_PERF_PASSWORD
)
if ($Database -notlike 'Ims_Perf*') { throw 'A perf database only.' }
if (-not $Password) { $Password = (Get-Content (Join-Path $PSScriptRoot '..\..\src\Ims.Api\appsettings.Development.json') -Raw | ConvertFrom-Json).Seed.AdminPassword }
Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
$http = New-Object System.Net.Http.HttpClient($handler)
$http.Timeout = [TimeSpan]::FromSeconds(125)
function Do-Login {
    $b = New-Object System.Net.Http.StringContent((@{ username = $User; password = $Password } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json')
    $j = $http.PostAsync("$BaseUrl/api/auth/login", $b).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    if (-not $j.accessToken) { throw 'login failed' }
    $http.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $j.accessToken)
}
Do-Login

$conn = New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=$Database;Integrated Security=true")
$conn.Open()
$readsSql = "EXEC sys.sp_query_store_flush_db; SELECT CAST(ISNULL(SUM(rs.avg_logical_io_reads * rs.count_executions), 0) AS bigint), CAST(ISNULL(SUM(rs.count_executions), 0) AS bigint) FROM sys.query_store_runtime_stats rs JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id JOIN sys.query_store_query q ON q.query_id = p.query_id JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id WHERE qt.query_sql_text NOT LIKE N'%query_store%'"
function Get-Reads {
    $c = $conn.CreateCommand(); $c.CommandText = $readsSql; $c.CommandTimeout = 120
    $r = $c.ExecuteReader(); [void]$r.Read(); $v = @([long]$r[0], [long]$r[1]); $r.Close(); $v
}
function Invoke-Get([string]$url) {
    $r = $http.GetAsync($url).GetAwaiter().GetResult(); $b = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    if ([int]$r.StatusCode -eq 401) { Do-Login; $r = $http.GetAsync($url).GetAwaiter().GetResult(); $b = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() }
    [int]$r.StatusCode
}

$rows = @()
foreach ($row in (Import-Csv $From)) {
    if ($row.Endpoint -notmatch $Only) { continue }
    $url = $BaseUrl + [uri]::EscapeUriString($row.Url)
    $n = if ($row.Endpoint -like 'export:*') { $ExportCalls } else { $Calls }
    $status = Invoke-Get $url                       # warm-up: plan compile, not counted
    $b = Get-Reads
    for ($i = 0; $i -lt $n; $i++) { $status = Invoke-Get $url }
    $a = Get-Reads
    $o = [pscustomobject]@{ Label = $Label; Endpoint = $row.Endpoint; Status = $status; Calls = $n
        ReadsPerCall = [long][math]::Round(($a[0] - $b[0]) / $n); StatementsPerCall = [math]::Round(($a[1] - $b[1]) / $n, 1) }
    $rows += $o
    '{0,-46} {1} {2,10} reads/call {3,5} statements/call' -f $o.Endpoint, $o.Status, $o.ReadsPerCall, $o.StatementsPerCall
}
$conn.Close()
$out = Join-Path $PSScriptRoot "results-$Label-reads.csv"
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $out
"saved $out"
