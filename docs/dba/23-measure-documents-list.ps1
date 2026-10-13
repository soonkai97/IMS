<#
  23-measure-documents-list.ps1 -- GET /api/documents through a running Ims.Api, page sizes 25 / 50 / 150 / 500 (the API cuts the list at Limits.MaxPageSize = 200),
  for several filters, in three call orders. The page statement used to be compiled for the first page size that arrived and that plan served every other
  size (README section 4d), so the order of the first calls decided how slow the rest were:
    small-first  the plan cache of this database is cleared, then sizes 25, 50, 150, 500
    large-first  cleared, then 500, 150, 50, 25
    poisoned     cleared, size 25 is asked once (the statement gets cached), the cache is cleared again and every cached page statement is executed ONCE from sqlcmd
                 with TOP(@p) = -PoisonSize (default 2000, where the optimizer picks the plan that scans StockDocumentLine; the API itself cannot ask for more
                 than 200 rows), then the sizes are asked 25, 50, 150, 500. This is the state an earlier compile for a large page leaves behind (a statistics
                 update, a restart followed by a large first page); it is how the defect is reproduced when the flip point of the optimizer is above 200 rows.
  Plans are cleared with ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE (this database only, no other database is touched).

  Usage (API running against a perf database, see docs/dba/README.md section 9; it should be the only user of the database):
    powershell -File docs/dba/23-measure-documents-list.ps1 -Label 1m-documents-list-after -BaseUrl http://localhost:5187 -Database Ims_Perf_Docs
  Output: one line per call, and docs/dba/results-<Label>.csv. Credentials as in 02-measure-api.ps1 (Seed:AdminPassword of appsettings.Development.json).
#>
param(
    [string]$BaseUrl = 'http://localhost:5187',
    [string]$Label = 'documents-list',
    [string]$Database = 'Ims_Perf',
    [string]$Server = '(localdb)\ImsDev',
    [int[]]$Sizes = @(25, 50, 150, 500),
    [int]$PoisonSize = 2000
)
if ($Database -notlike 'Ims_Perf*') { throw 'A perf database only.' }
$dev = Join-Path $PSScriptRoot '..\..\src\Ims.Api\appsettings.Development.json'
$password = (Get-Content $dev -Raw | ConvertFrom-Json).Seed.AdminPassword
$login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/auth/login" -ContentType 'application/json' -Body (@{ username = 'admin'; password = $password } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.accessToken)" }

function Clear-Plans { sqlcmd -S $Server -E -d $Database -Q 'ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE' | Out-Null }
function Get-Page([string]$query) { Invoke-RestMethod -Uri "$BaseUrl/api/documents?$query" -Headers $headers }

# Executes every cached page statement with only int parameters once with TOP(@p) = $PoisonSize, in the same session options as the API (ARITHABORT OFF).
$primeSql = @"
SET NOCOUNT ON; SET ARITHABORT OFF; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON;
DECLARE @texts TABLE (t nvarchar(max));
INSERT @texts SELECT st.text FROM sys.dm_exec_cached_plans cp CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
WHERE cp.objtype = 'Prepared' AND st.text LIKE N'(@p int%)SELECT%FROM [[]StockDocumentLine]%TOP(@p)%' AND st.text NOT LIKE N'%nvarchar%' AND st.text NOT LIKE N'%dm_exec%';
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
DECLARE @t nvarchar(max), @decl nvarchar(400), @args nvarchar(max), @stmt nvarchar(max), @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT t FROM @texts;
OPEN c; FETCH NEXT FROM c INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @decl = SUBSTRING(@t, 2, CHARINDEX(N')SELECT', @t) - 2);
    SET @stmt = SUBSTRING(@t, CHARINDEX(N')SELECT', @t) + 1, LEN(@t));
    SELECT @args = STRING_AGG(CONCAT(n, N' = ', CASE n WHEN N'@p' THEN CAST($PoisonSize AS nvarchar(10)) WHEN N'@w' THEN N'2' WHEN N'@s' THEN N'3' WHEN N'@t' THEN N'2' WHEN N'@olderThan' THEN N'100000' ELSE N'NULL' END), N', ')
    FROM (SELECT LEFT(LTRIM(value), CHARINDEX(N' ', LTRIM(value)) - 1) AS n FROM STRING_SPLIT(@decl, N',')) x;
    SET @sql = N'EXEC sp_executesql @stmt, @decl, ' + @args;
    EXEC sp_executesql @sql, N'@stmt nvarchar(max), @decl nvarchar(400)', @stmt, @decl;
    FETCH NEXT FROM c INTO @t;
END
"@
function Set-PoisonedPlans {
    # The parameter names the page statements use: @p (take), @w (warehouse), @t (type), @s (status), @olderThan (cursor); any other parameter gets NULL.
    $file = Join-Path $env:TEMP "ims-prime-$PID.sql"
    $primeSql | Set-Content $file -Encoding utf8
    sqlcmd -S $Server -E -d $Database -i $file -o "$file.out" | Out-Null
    Remove-Item $file, "$file.out" -ErrorAction SilentlyContinue
}

# name -> query string without pageSize. 'CURSOR' variants take the cursor of the same filter's page 3 (50 rows per page).
$variants = [ordered]@{
    'no filter'               = ''
    'warehouse'               = 'warehouseId=2'
    'status posted'           = 'status=Posted'
    'status draft (rare)'     = 'status=Draft'
    'type + status'           = 'type=Issue&status=Posted'
    'search "seed" (remark)'  = 'search=seed'
    'search "ISS-WH01-26"'    = 'search=ISS-WH01-26'
    'page 2 (offset)'         = 'page=2'
    'cursor'                  = 'CURSOR'
    'warehouse + cursor'      = 'warehouseId=2&CURSOR'
}

$rows = New-Object System.Collections.Generic.List[object]
foreach ($name in $variants.Keys) {
    $q = $variants[$name]
    if ($q -like '*CURSOR*') {
        $base = $q.Replace('CURSOR', '').TrimEnd('&')
        $seed = Get-Page ((@($base, 'page=3', 'pageSize=50', 'includeTotal=false') | Where-Object { $_ }) -join '&')
        if (-not $seed.nextCursor) { '{0,-26} skipped: no cursor' -f $name; continue }
        $q = (@($base, "cursor=$([uri]::EscapeDataString($seed.nextCursor))") | Where-Object { $_ }) -join '&'
    }
    foreach ($order in 'small-first', 'large-first', 'poisoned') {
        $list = if ($order -eq 'large-first') { $Sizes | Sort-Object -Descending } else { $Sizes }
        Clear-Plans
        if ($order -eq 'poisoned') {
            Get-Page ((@($q, 'pageSize=25') | Where-Object { $_ }) -join '&') | Out-Null
            Set-PoisonedPlans
        }
        foreach ($size in $list) {
            $sw = [Diagnostics.Stopwatch]::StartNew()
            $r = Get-Page ((@($q, "pageSize=$size") | Where-Object { $_ }) -join '&')
            $sw.Stop()
            $rows.Add([pscustomobject]@{ Label = $Label; Variant = $name; Order = $order; PageSize = $size; Items = @($r.items).Count; Ms = $sw.ElapsedMilliseconds })
            '{0,-26} {1,-12} size {2,4}  {3,4} rows  {4,6} ms' -f $name, $order, $size, @($r.items).Count, $sw.ElapsedMilliseconds
        }
    }
}
$out = Join-Path $PSScriptRoot "results-$Label.csv"
$rows | Export-Csv -Path $out -NoTypeInformation
"written $out"
