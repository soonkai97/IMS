<#
  21-by-id-reads.ps1 -- the by-id endpoints, integer-key code against public-id code on the SAME database and the SAME rows.

  Two APIs run against one database that has the PublicId columns (the AddPublicIds migration is applied): the code before the switch (-OldUrl; it ignores the
  column and addresses rows by the integer key) and the code after it (-NewUrl; it addresses them by the GUID). Each entity is picked in SQL, so both
  calls read the same row. For every endpoint: one untimed call, then -Calls timed calls alternating old and new; prints the logical reads per call (Query Store,
  the whole request) and the median and maximum wall time. Output: docs/dba/results-<Label>.csv.
  Needs: Query Store ON with QUERY_CAPTURE_MODE = ALL, only these two APIs using the database, an attachment on a product and on a document (it uploads
  a 68-byte PNG through the new API when there is none), credentials as in 02-measure-api.ps1.

    powershell -File docs/dba/21-by-id-reads.ps1 -OldUrl http://localhost:5612 -NewUrl http://localhost:5611 -Database Ims_Perf_X -Label 1m-by-id
#>
param(
    [Parameter(Mandatory)][string]$OldUrl,
    [Parameter(Mandatory)][string]$NewUrl,
    [Parameter(Mandatory)][string]$Database,
    [string]$Server = '(localdb)\ImsDev',
    [string]$Label = 'by-id',
    [int]$Calls = 15,
    [string]$User = $(if ($env:IMS_PERF_USER) { $env:IMS_PERF_USER } else { 'admin' }),
    [string]$Password = $env:IMS_PERF_PASSWORD
)
if ($Database -notlike 'Ims_Perf*') { throw 'A perf database only.' }
if (-not $Password) { $Password = (Get-Content (Join-Path $PSScriptRoot '..\..\src\Ims.Api\appsettings.Development.json') -Raw | ConvertFrom-Json).Seed.AdminPassword }
Add-Type -AssemblyName System.Net.Http
$conn = New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=$Database;Integrated Security=true"); $conn.Open()
function Sql([string]$q) { $c = $conn.CreateCommand(); $c.CommandText = $q; $c.CommandTimeout = 120; $r = $c.ExecuteReader(); $t = New-Object System.Data.DataTable; $t.Load($r); , $t }   # the comma keeps PowerShell from unrolling the table into rows
function Get-Reads { (Sql "EXEC sys.sp_query_store_flush_db; SELECT CAST(ISNULL(SUM(rs.avg_logical_io_reads * rs.count_executions), 0) AS bigint) AS r FROM sys.query_store_runtime_stats rs JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id JOIN sys.query_store_query q ON q.query_id = p.query_id JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id WHERE qt.query_sql_text NOT LIKE N'%query_store%'").Rows[0].r }

function New-Client([string]$base) {
    $h = New-Object System.Net.Http.HttpClientHandler; $h.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip
    $c = New-Object System.Net.Http.HttpClient($h); $c.Timeout = [TimeSpan]::FromSeconds(120)
    $b = New-Object System.Net.Http.StringContent((@{ username = $User; password = $Password } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json')
    $j = $c.PostAsync("$base/api/auth/login", $b).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $c.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $j.accessToken); $c
}
$old = New-Client $OldUrl; $new = New-Client $NewUrl

# entities, by SQL
$hot = (Sql "SELECT TOP 1 p.Id, p.PublicId FROM Product p WHERE p.Sku = N'SKU-00001'").Rows[0]
$mid = (Sql "SELECT TOP 1 p.Id, p.PublicId FROM Product p WHERE p.Sku = N'SKU-00159'").Rows[0]
$wh2 = (Sql "SELECT TOP 1 Id, PublicId FROM Warehouse WHERE Code = N'WH02'").Rows[0]
$big = (Sql "SELECT TOP 1 d.Id, d.PublicId FROM StockDocument d JOIN (SELECT TOP 1 DocumentId FROM StockDocumentLine GROUP BY DocumentId ORDER BY COUNT(*) DESC) x ON x.DocumentId = d.Id").Rows[0]
$med = (Sql "SELECT TOP 1 d.Id, d.PublicId FROM StockDocument d WHERE d.Status = 3 AND (SELECT COUNT(*) FROM StockDocumentLine l WHERE l.DocumentId = d.Id) BETWEEN 5 AND 8 ORDER BY d.Id DESC").Rows[0]
$lines = @{ big = (Sql "SELECT COUNT(*) AS n FROM StockDocumentLine WHERE DocumentId = $($big.Id)").Rows[0].n; med = (Sql "SELECT COUNT(*) AS n FROM StockDocumentLine WHERE DocumentId = $($med.Id)").Rows[0].n }
"entities: hot product $($hot.Id), mid $($mid.Id), warehouse $($wh2.Id), document with most lines $($big.Id) ($($lines.big) lines), median document $($med.Id) ($($lines.med) lines)"

# attachments: one on the hot product and one on the median document (uploaded through the new API when missing)
function Ensure-Attachment([int]$ownerType, [guid]$ownerPublicId) {
    $have = Sql "SELECT COUNT(*) AS n FROM Attachment WHERE OwnerType = $ownerType AND OwnerId = (SELECT Id FROM $(if ($ownerType -eq 2) { 'Product' } else { 'StockDocument' }) WHERE PublicId = '$ownerPublicId')"
    if ($have.Rows[0].n -gt 0) { return }
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
    $form = New-Object System.Net.Http.MultipartFormDataContent
    $form.Add((New-Object System.Net.Http.StringContent([string]$ownerType)), 'ownerType'); $form.Add((New-Object System.Net.Http.StringContent([string]$ownerPublicId)), 'ownerId')
    $f = New-Object System.Net.Http.ByteArrayContent(,$png); $f.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png'); $form.Add($f, 'file', 'pixel.png')
    $r = $new.PostAsync("$NewUrl/api/attachments", $form).GetAwaiter().GetResult(); "upload owner type $ownerType -> $([int]$r.StatusCode)"
}
Ensure-Attachment 2 $hot.PublicId; Ensure-Attachment 1 $med.PublicId

$cases = [ordered]@{
    'documents/{id} (most lines)'      = @("/api/documents/$($big.Id)", "/api/documents/$($big.PublicId)")
    'documents/{id} (median posted)'   = @("/api/documents/$($med.Id)", "/api/documents/$($med.PublicId)")
    'products/{id} (hot)'              = @("/api/products/$($hot.Id)", "/api/products/$($hot.PublicId)")
    'products/{id} (mid)'              = @("/api/products/$($mid.Id)", "/api/products/$($mid.PublicId)")
    'stock?productId (hot)'            = @("/api/stock?productId=$($hot.Id)", "/api/stock?productId=$($hot.PublicId)")
    'stock?warehouseId=WH02'           = @("/api/stock?warehouseId=$($wh2.Id)", "/api/stock?warehouseId=$($wh2.PublicId)")
    'ledger?productId (hot)'           = @("/api/stock/ledger?productId=$($hot.Id)", "/api/stock/ledger?productId=$($hot.PublicId)")
    'ledger?productId (mid)'           = @("/api/stock/ledger?productId=$($mid.Id)", "/api/stock/ledger?productId=$($mid.PublicId)")
    'ledger?productId+warehouse'       = @("/api/stock/ledger?productId=$($hot.Id)&warehouseId=$($wh2.Id)", "/api/stock/ledger?productId=$($hot.PublicId)&warehouseId=$($wh2.PublicId)")
    'ledger?sourceDocId'               = @("/api/stock/ledger?sourceDocId=$($med.Id)", "/api/stock/ledger?sourceDocId=$($med.PublicId)")
    'movements?productId (hot)'        = @("/api/reports/movements?productId=$($hot.Id)", "/api/reports/movements?productId=$($hot.PublicId)")
    'movements?productId (mid)'        = @("/api/reports/movements?productId=$($mid.Id)", "/api/reports/movements?productId=$($mid.PublicId)")
    'attachments?product'              = @("/api/attachments?ownerType=Product&ownerId=$($hot.Id)", "/api/attachments?ownerType=Product&ownerId=$($hot.PublicId)")
    'attachments?document'             = @("/api/attachments?ownerType=Document&ownerId=$($med.Id)", "/api/attachments?ownerType=Document&ownerId=$($med.PublicId)")
    'documents?warehouseId=WH02'       = @("/api/documents?warehouseId=$($wh2.Id)&type=1", "/api/documents?warehouseId=$($wh2.PublicId)&type=1")
}
$attOld = (Sql "SELECT TOP 1 a.Id, a.PublicId FROM Attachment a WHERE a.OwnerType = 2 ORDER BY a.Id").Rows[0]
if ($attOld) { $cases['attachments/{id} (download)'] = @("/api/attachments/$($attOld.Id)", "/api/attachments/$($attOld.PublicId)") }

function Time-Get($client, [string]$url) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); $r = $client.GetAsync($url).GetAwaiter().GetResult(); [void]$r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(); $sw.Stop()
    [pscustomobject]@{ Status = [int]$r.StatusCode; Ms = $sw.Elapsed.TotalMilliseconds }
}
function Median([double[]]$a) { $s = $a | Sort-Object; $s[[math]::Floor(($s.Count - 1) / 2)] }
$rows = @()
foreach ($name in $cases.Keys) {
    $u = $cases[$name]
    [void](Time-Get $old ($OldUrl + $u[0])); [void](Time-Get $new ($NewUrl + $u[1]))      # warm-up: plan compile
    $tOld = @(); $tNew = @(); $rOld = 0; $rNew = 0; $sOld = 0; $sNew = 0
    for ($i = 0; $i -lt $Calls; $i++) {
        $b = Get-Reads; $x = Time-Get $old ($OldUrl + $u[0]); $a = Get-Reads; $rOld += ($a - $b); $tOld += $x.Ms; $sOld = $x.Status
        $b = Get-Reads; $x = Time-Get $new ($NewUrl + $u[1]); $a = Get-Reads; $rNew += ($a - $b); $tNew += $x.Ms; $sNew = $x.Status
    }
    $o = [pscustomobject]@{ Label = $Label; Endpoint = $name; StatusOld = $sOld; StatusNew = $sNew
        ReadsOld = [long][math]::Round($rOld / $Calls); ReadsNew = [long][math]::Round($rNew / $Calls)
        MedianMsOld = [math]::Round((Median $tOld), 1); MedianMsNew = [math]::Round((Median $tNew), 1)
        MaxMsOld = [math]::Round(($tOld | Measure-Object -Maximum).Maximum, 1); MaxMsNew = [math]::Round(($tNew | Measure-Object -Maximum).Maximum, 1) }
    $rows += $o
    '{0,-34} {1}/{2}  reads {3,7} -> {4,7}   median ms {5,7} -> {6,7}' -f $name, $o.StatusOld, $o.StatusNew, $o.ReadsOld, $o.ReadsNew, $o.MedianMsOld, $o.MedianMsNew
}
$conn.Close()
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $PSScriptRoot "results-$Label.csv")
