<#
  run-variant.ps1 -- measures ONE key strategy end to end in a scratch database on (localdb)\ImsDev and appends the results to results\*.csv.
    A1  uniqueidentifier clustered PK, NEWSEQUENTIALID() default        A2  uniqueidentifier, v7-like value inserted as is
    A3  uniqueidentifier, NEWID()                                        B1  bigint/int identity PK + unique public GUID (sequential)
    B2  identity PK + unique public GUID (v7-like, as is)                C   identity (the product today)
  For every tier (ledger rows): organic load with concurrent writers (the posting path, 20-posting-proc.sql) -> sizes, hot queries, fragmentation
  -> full backup -> restore copy 1 (timed): shift workload from a cold cache (buffer pool footprint), 3 throughput windows with log analysis, cursor walks
  -> restore copy 2 (timed): rebuild all indexes (timed), sizes and hot queries again ("rebuilt").  The main database then continues to the next tier.
  Run on a quiet machine, one variant at a time:
     powershell -File run-variant.ps1 -Variant A1
  Scratch databases are named Ims_PerfGuid_<Variant> and dropped at the end (use -KeepDb to keep the main one). Needs ~25 GB free in DataDir.
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('A1', 'A2', 'A3', 'B1', 'B2', 'C')][string]$Variant,
    [int64[]]$Tiers = @(1000000, 3000000),
    [int]$Products = 50000,
    [int]$Writers = 16,
    [int]$ChunkTx = 2500,
    [int]$WindowTx = 1000,           # transactions per writer in one throughput window (16 x 1000 = 16,000 posting transactions)
    [int]$Reps = 7,
    [string]$DataDir = (Join-Path $env:TEMP 'ims_guidpk'),
    [string]$Results = (Join-Path $PSScriptRoot 'results'),
    [switch]$KeepDb,
    [switch]$NoCopies
)
$ErrorActionPreference = 'Stop'
$script:DataDir = $DataDir
. (Join-Path $PSScriptRoot 'lib.ps1')
New-Item -ItemType Directory -Force -Path $Results, (Join-Path $DataDir 'bk') | Out-Null
$db = "Ims_PerfGuid_$Variant"
$logFile = Join-Path $env:TEMP "guidpk_$Variant.log"
function Log([string]$m) { $l = "{0:HH:mm:ss} [{1}] {2}" -f (Get-Date), $Variant, $m; Write-Host $l; Add-Content -Path $logFile -Value $l }
function Csv([string]$name, $dt, [hashtable]$extra) { Export-Table $dt (Join-Path $Results "$name.csv") $extra }
function Scalar([string]$d, [string]$sql) { (Invoke-Q $d $sql).Rows[0][0] }
function Ledger-Rows([string]$d) { [int64](Scalar $d "SELECT SUM(row_count) FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID(N'dbo.StockLedgerEntry') AND index_id = 1") }
function Context([string]$step, [int64]$tier) {
    $c = Get-CpuContext
    $o = [pscustomobject]@{ variant = $Variant; tier = $tier; step = $step; at_utc = (Get-Date).ToUniversalTime().ToString('s'); cpu_total_pct_3s = $c.cpu_total_pct }
    $o | Export-Csv -Path (Join-Path $Results 'context.csv') -NoTypeInformation -Append -Encoding ASCII
}
function Start-SqlFileAsync([string]$database, [string[]]$files, [hashtable]$vars, [string]$out) {
    $wrapper = Join-Path $env:TEMP ("guidpk_wrapa_{0}.sqlcmd" -f [guid]::NewGuid().ToString('N'))
    $lines = @(":setvar Db $database", ":setvar DataDir `"$DataDir`"")
    foreach ($k in $vars.Keys) { $lines += ":setvar $k `"$($vars[$k])`"" }
    $lines += ":r `"$(Join-Path $PSScriptRoot "variants\$Variant.sqlcmd")`""
    foreach ($f in $files) { $lines += ":r `"$(Join-Path $PSScriptRoot $f)`"" }
    Set-Content -Path $wrapper -Value $lines -Encoding ASCII
    Start-Process sqlcmd -ArgumentList @('-S', "`"$script:Server`"", '-E', '-b', '-I', '-d', $database, '-i', "`"$wrapper`"", '-o', "`"$out`"") -PassThru -WindowStyle Hidden
}
function Update-AllStats([string]$d) {
    foreach ($t in 'Product', 'ProductBarcode', 'StockBalance', 'StockDocument', 'StockDocumentLine', 'StockLedgerEntry', 'AuditLogs') { Invoke-NonQ $d "UPDATE STATISTICS dbo.$t WITH FULLSCAN" }
}

# ------------------------------------------------------------------ measurement of the current state of one database
function Measure-State([string]$d, [int64]$tier, [string]$state) {
    $x = @{ variant = $Variant; tier = $tier; state = $state }
    Log "  [$state] sizes"
    Csv 'sizes' (Invoke-Q $d (Get-Content (Join-Path $PSScriptRoot '30-sizes.sql') -Raw)) $x
    Log "  [$state] hot queries"
    $out = Join-Path $env:TEMP "guidpk_q_$Variant.txt"
    Invoke-SqlFiles $Variant $d @('40-hot-queries.sql') @{ Tag = "$Variant/$tier/$state"; Reps = $Reps } $out | Out-Null
    [System.IO.File]::Delete($out)
    $sum = Invoke-Q $d @"
SELECT Q AS q, PSet AS pset,
       MAX(CASE WHEN Rep = 0 THEN LogicalReads END) AS cold_logical_reads, MAX(CASE WHEN Rep = 0 THEN PhysicalReads END) AS cold_physical_reads,
       MAX(CASE WHEN Rep = 0 THEN ElapsedUs END) / 1000.0 AS cold_ms,
       MAX(CASE WHEN Rep = 1 THEN LogicalReads END) AS warm_logical_reads, MAX(CASE WHEN Rep = 1 THEN PhysicalReads END) AS warm_physical_reads,
       AVG(CASE WHEN Rep >= 1 THEN CAST(ElapsedUs AS float) END) / 1000.0 AS warm_mean_ms, MIN(CASE WHEN Rep >= 1 THEN ElapsedUs END) / 1000.0 AS warm_min_ms
FROM dbo._QueryResults WHERE Tag = '$Variant/$tier/$state' GROUP BY Q, PSet ORDER BY Q, PSet
"@
    Csv 'queries' $sum $x
    Invoke-NonQ $d "DELETE FROM dbo._QueryResults WHERE Tag = '$Variant/$tier/$state'"
    Log "  [$state] fragmentation (DETAILED)"
    Csv 'frag' (Invoke-Q $d (Get-Content (Join-Path $PSScriptRoot '31-frag.sql') -Raw)) $x
}

function Restore-Copy([string]$copy, [string]$bak, [string]$src) {
    Drop-Db $copy
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-NonQ 'master' "RESTORE DATABASE [$copy] FROM DISK = N'$bak' WITH CHECKSUM, REPLACE, MOVE N'$src' TO N'$DataDir\$copy.mdf', MOVE N'${src}_log' TO N'$DataDir\${copy}_log.ldf'"
    $sw.ElapsedMilliseconds
}

# ------------------------------------------------------------------ main
Log "start: variant $Variant, tiers $($Tiers -join ','), products $Products, writers $Writers, data dir $DataDir"
$ver = Invoke-Q 'master' "SELECT @@VERSION AS v, SERVERPROPERTY('Edition') AS edition, (SELECT cpu_count FROM sys.dm_os_sys_info) AS logical_cpus, (SELECT physical_memory_kb / 1024 FROM sys.dm_os_sys_info) AS memory_mb"
Csv 'environment' $ver @{ variant = $Variant; at_utc = (Get-Date).ToUniversalTime().ToString('s') }

Drop-Db $db
Invoke-SqlFiles $Variant 'master' @('09-create-db.sql') @{ Db = $db } | Out-Null
Invoke-SqlFiles $Variant $db @('10-schema.sql') | Out-Null
Invoke-SqlFiles $Variant $db @('11-seed-master.sql') @{ Products = $Products } | Out-Null
Invoke-SqlFiles $Variant $db @('20-posting-proc.sql') | Out-Null
Log "database $db created and seeded"

$chunk = 0
foreach ($tier in $Tiers) {
  try {
    Log "=== tier $tier ledger rows: organic load"
    Context 'load-start' $tier
    while ($true) {
        $cur = Ledger-Rows $db
        if ($cur -ge $tier) { break }
        $needTx = [math]::Ceiling(($tier - $cur) / 5.5)
        $per = [int][math]::Min($ChunkTx, [math]::Max(1, [math]::Ceiling($needTx / $Writers)))
        $r = Invoke-Writers $db "load_${tier}_$chunk" $Writers $per -products $Products
        $now = Ledger-Rows $db
        Log ("  chunk {0}: {1} tx, {2} retries, {3} ms, {4} tx/s, ledger rows {5}" -f $chunk, $r.tx, $r.retries, $r.ms, $r.tx_per_s, $now)
        [pscustomobject]@{ variant = $Variant; tier = $tier; chunk = $chunk; writers = $r.writers; tx = $r.tx; retries = $r.retries; ms = $r.ms; tx_per_s = $r.tx_per_s; ledger_rows_after = $now } |
            Export-Csv -Path (Join-Path $Results 'load.csv') -NoTypeInformation -Append -Encoding ASCII
        $chunk++
    }
    $counts = Invoke-Q $db "SELECT (SELECT COUNT_BIG(*) FROM dbo.StockLedgerEntry) AS ledger_rows, (SELECT COUNT_BIG(*) FROM dbo.StockDocument) AS documents, (SELECT COUNT_BIG(*) FROM dbo.StockDocumentLine) AS document_lines, (SELECT COUNT_BIG(*) FROM dbo.AuditLogs) AS audit_rows, (SELECT COUNT_BIG(*) FROM dbo.StockBalance) AS balance_rows, (SELECT COUNT_BIG(*) FROM dbo.Product) AS products"
    Csv 'rowcounts' $counts @{ variant = $Variant; tier = $tier }
    Log ("  rows: " + (($counts.Rows[0].ItemArray) -join ' / '))

    Log "=== tier $tier : statistics, then measurements of the organically loaded state"
    Update-AllStats $db
    Invoke-NonQ $db "CHECKPOINT"
    Context 'measure-asloaded' $tier
    Measure-State $db $tier 'asloaded'

    if ($NoCopies) { continue }

    Log "=== tier $tier : backup"
    $bak = Join-Path $DataDir "bk\$db.bak"
    if (Test-Path -LiteralPath $bak) { [System.IO.File]::Delete($bak) }
    Invoke-NonQ $db "CHECKPOINT"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-NonQ 'master' "BACKUP DATABASE [$db] TO DISK = N'$bak' WITH INIT, CHECKSUM"
    $bms = $sw.ElapsedMilliseconds
    $bmb = [math]::Round((Get-Item -LiteralPath $bak).Length / 1MB, 1)
    $dfile = [double](Scalar 'master' "SELECT SUM(size) * 8 / 1024.0 FROM sys.master_files WHERE database_id = DB_ID(N'$db') AND type = 0")
    $used = [double](Scalar $db "SELECT SUM(allocated_extent_page_count) * 8 / 1024.0 FROM sys.dm_db_file_space_usage")
    Log "  backup $bmb MB in $bms ms (data file $([math]::Round($dfile)) MB, allocated $([math]::Round($used)) MB)"

    Log "=== tier $tier : restore copy 1 (workload, throughput, cursor walks)"
    $c1 = "${db}_c1"
    $rms = Restore-Copy $c1 $bak $db
    Log "  restore $rms ms"
    [pscustomobject]@{ variant = $Variant; tier = $tier; backup_mb = $bmb; backup_ms = $bms; restore_ms = $rms; data_file_mb = [math]::Round($dfile, 1); allocated_mb = [math]::Round($used, 1) } |
        Export-Csv -Path (Join-Path $Results 'backup-restore.csv') -NoTypeInformation -Append -Encoding ASCII

    # --- shift workload from a cold cache: 4 readers + 4 writers, then what the buffer pool holds
    Log "  buffer pool workload"
    Invoke-NonQ $c1 "CHECKPOINT"; Invoke-NonQ $c1 "DBCC DROPCLEANBUFFERS WITH NO_INFOMSGS"
    Context 'workload' $tier
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $readers = @(); 1..4 | ForEach-Object { $readers += Start-SqlFileAsync $c1 @('61-read-mix.sql') @{ Ops = 1500; Seed = (1000 + $_) } (Join-Path $env:TEMP "guidpk_rd$_.txt") }
    $wl = Invoke-Writers $c1 'workload' 4 2500 -products $Products
    $readers | Wait-Process
    $wms = $sw.ElapsedMilliseconds
    if (@($readers | Where-Object { $_.ExitCode -ne 0 }).Count) { throw "reader failed" }
    foreach ($i in 1..4) { [System.IO.File]::Delete((Join-Path $env:TEMP "guidpk_rd$i.txt")) }
    $bp = Invoke-Q $c1 (Get-Content (Join-Path $PSScriptRoot '62-buffer-pool.sql') -Raw)
    Csv 'bufferpool' $bp @{ variant = $Variant; tier = $tier }
    $tot = ($bp.Rows | Where-Object { $_.tbl -eq '(all)' }).cached_mb
    $maxmb = Scalar 'master' "SELECT value_in_use FROM sys.configurations WHERE name = 'max server memory (MB)'"
    [pscustomobject]@{ variant = $Variant; tier = $tier; readers = 4; reads_each = 1500; writers = 4; tx_each = 2500; wall_ms = $wms; writer_tx_per_s = $wl.tx_per_s; cached_mb_total = $tot } |
        Export-Csv -Path (Join-Path $Results 'workload.csv') -NoTypeInformation -Append -Encoding ASCII
    Log "  workload $wms ms, cached $tot MB"

    # --- throughput windows with log analysis (FULL recovery after a full backup to NUL, so the log keeps everything of the window)
    Log "  throughput windows"
    Invoke-NonQ $c1 "ALTER DATABASE [$c1] SET RECOVERY FULL"
    Invoke-NonQ 'master' "BACKUP DATABASE [$c1] TO DISK = N'NUL' WITH INIT"
    $grow = [math]::Ceiling((Scalar $c1 "SELECT SUM(size) * 8 / 1024.0 FROM sys.database_files WHERE type = 0") + 1500)
    Invoke-NonQ 'master' "ALTER DATABASE [$c1] MODIFY FILE (NAME = N'$db', SIZE = ${grow}MB)"
    Invoke-NonQ 'master' "ALTER DATABASE [$c1] MODIFY FILE (NAME = N'${db}_log', SIZE = 3GB)"
    foreach ($w in 1..3) {
        Invoke-NonQ 'master' "BACKUP LOG [$c1] TO DISK = N'NUL'"
        Invoke-NonQ $c1 "CHECKPOINT"
        $snap0 = Invoke-Q $c1 @"
SELECT OBJECT_NAME(s.object_id) + N'.' + i.name AS idx, s.leaf_allocation_count AS allocs, s.page_lock_wait_count AS page_lock_waits, s.row_lock_wait_count AS row_lock_waits
FROM sys.dm_db_index_operational_stats(DB_ID(), NULL, NULL, NULL) s JOIN sys.indexes i ON i.object_id = s.object_id AND i.index_id = s.index_id WHERE OBJECTPROPERTY(s.object_id, 'IsUserTable') = 1 AND OBJECT_NAME(s.object_id) NOT LIKE N'[_]%'
"@
        $lg0 = [int64](Scalar $c1 "SELECT num_of_bytes_written FROM sys.dm_io_virtual_file_stats(DB_ID(), 2)")
        $dw0 = [int64](Scalar $c1 "SELECT num_of_bytes_written FROM sys.dm_io_virtual_file_stats(DB_ID(), 1)")
        Context "window$w" $tier
        $cpu = (Get-CpuContext).cpu_total_pct
        $sqlPid = [int](Scalar 'master' "SELECT CAST(SERVERPROPERTY('ProcessID') AS int)")
        $cpu0 = (Get-Process -Id $sqlPid).TotalProcessorTime.TotalMilliseconds
        $r = Invoke-Writers $c1 "window$w" $Writers $WindowTx -products $Products
        $sqlCpuMs = (Get-Process -Id $sqlPid).TotalProcessorTime.TotalMilliseconds - $cpu0
        $lg1 = [int64](Scalar $c1 "SELECT num_of_bytes_written FROM sys.dm_io_virtual_file_stats(DB_ID(), 2)")
        $dw1 = [int64](Scalar $c1 "SELECT num_of_bytes_written FROM sys.dm_io_virtual_file_stats(DB_ID(), 1)")
        $snap1 = Invoke-Q $c1 @"
SELECT OBJECT_NAME(s.object_id) + N'.' + i.name AS idx, s.leaf_allocation_count AS allocs, s.page_lock_wait_count AS page_lock_waits, s.row_lock_wait_count AS row_lock_waits
FROM sys.dm_db_index_operational_stats(DB_ID(), NULL, NULL, NULL) s JOIN sys.indexes i ON i.object_id = s.object_id AND i.index_id = s.index_id WHERE OBJECTPROPERTY(s.object_id, 'IsUserTable') = 1 AND OBJECT_NAME(s.object_id) NOT LIKE N'[_]%'
"@
        # per index: leaf page allocations (appends and splits) during the window
        $m0 = @{}; foreach ($row in $snap0.Rows) { $m0[$row.idx] = $row }
        $rows = foreach ($row in $snap1.Rows) { $b = $m0[$row.idx]; [pscustomobject]@{ variant = $Variant; tier = $tier; window = $w; idx = $row.idx; leaf_allocations = [int64]$row.allocs - $(if ($b) { [int64]$b.allocs } else { 0 }); page_lock_waits = [int64]$row.page_lock_waits - $(if ($b) { [int64]$b.page_lock_waits } else { 0 }); row_lock_waits = [int64]$row.row_lock_waits - $(if ($b) { [int64]$b.row_lock_waits } else { 0 }) } }
        $rows | Where-Object { $_.leaf_allocations -ne 0 -or $_.page_lock_waits -ne 0 -or $_.row_lock_waits -ne 0 } | Export-Csv -Path (Join-Path $Results 'throughput-by-index.csv') -NoTypeInformation -Append -Encoding ASCII
        # page allocations of the leaf level (new page at the end of an index or a split) over all indexes, and log volume from the log file counters
        $alloc = ($rows | Measure-Object leaf_allocations -Sum).Sum
        [pscustomobject]@{
            variant = $Variant; tier = $tier; window = $w; writers = $r.writers; tx = $r.tx; retries = $r.retries; ms = $r.ms; tx_per_s = $r.tx_per_s
            ledger_rows_per_s = [math]::Round($r.tx * 5.6 / ($r.ms / 1000.0), 0)
            leaf_allocations = $alloc; leaf_allocations_per_s = [math]::Round($alloc / ($r.ms / 1000.0), 0); leaf_allocations_per_tx = [math]::Round($alloc / $r.tx, 3)
            log_bytes_per_tx = [math]::Round(($lg1 - $lg0) / $r.tx, 0); data_file_bytes_written = $dw1 - $dw0
            sql_cpu_ms_per_tx = [math]::Round($sqlCpuMs / $r.tx, 2); cpu_total_pct_before = $cpu
        } | Export-Csv -Path (Join-Path $Results 'throughput.csv') -NoTypeInformation -Append -Encoding ASCII
        Log ("  window {0}: {1} tx/s, {2} retries, {3} leaf allocations/tx, {4} log bytes/tx, {5} sql cpu ms/tx" -f $w, $r.tx_per_s, $r.retries, [math]::Round($alloc / $r.tx, 2), [math]::Round(($lg1 - $lg0) / $r.tx), [math]::Round($sqlCpuMs / $r.tx, 2))
    }
    # --- small sample window, read from the transaction log (fn_dblog is slow, so only 800 transactions): LOP_DELETE_SPLIT = a split in the middle of an index, which moves rows
    Invoke-NonQ 'master' "BACKUP LOG [$c1] TO DISK = N'NUL'"
    Invoke-NonQ $c1 "CHECKPOINT"
    $sr = Invoke-Writers $c1 'splitsample' $Writers 50 -products $Products
    $lgs = (Invoke-Q $c1 "SELECT COUNT_BIG(*) AS log_records, SUM(CAST([Log Record Length] AS bigint)) AS log_bytes, SUM(CASE WHEN Operation = 'LOP_DELETE_SPLIT' THEN 1 ELSE 0 END) AS mid_page_splits FROM fn_dblog(NULL, NULL)").Rows[0]
    Csv 'splits-by-index' (Invoke-Q $c1 "SELECT AllocUnitName AS alloc_unit, COUNT(*) AS mid_page_splits FROM fn_dblog(NULL, NULL) WHERE Operation = 'LOP_DELETE_SPLIT' GROUP BY AllocUnitName ORDER BY 2 DESC") @{ variant = $Variant; tier = $tier; tx = $sr.tx }
    [pscustomobject]@{ variant = $Variant; tier = $tier; tx = $sr.tx; log_records = [int64]$lgs.log_records; log_bytes = [int64]$lgs.log_bytes; mid_page_splits = [int64]$lgs.mid_page_splits; mid_page_splits_per_tx = [math]::Round([double]$lgs.mid_page_splits / $sr.tx, 3) } |
        Export-Csv -Path (Join-Path $Results 'splits-sample.csv') -NoTypeInformation -Append -Encoding ASCII
    Log ("  split sample: {0} tx, {1} mid-page splits ({2}/tx)" -f $sr.tx, $lgs.mid_page_splits, [math]::Round([double]$lgs.mid_page_splits / $sr.tx, 3))
    Invoke-NonQ $c1 "ALTER DATABASE [$c1] SET RECOVERY SIMPLE"
    Log "  fragmentation after the windows"
    Csv 'frag' (Invoke-Q $c1 (Get-Content (Join-Path $PSScriptRoot '31-frag.sql') -Raw)) @{ variant = $Variant; tier = $tier; state = 'after-windows' }

    # --- cursor walks (writes: extra index, Reason 100+ rows) -- last on the copy
    Log "  cursor walks"
    $out = Join-Path $env:TEMP "guidpk_cw_$Variant.txt"
    Invoke-SqlFiles $Variant $c1 @('50-cursor-walk.sql') @{ Tag = "$Variant/$tier" } $out | Out-Null
    [System.IO.File]::Delete($out)
    Csv 'cursor-walk' (Invoke-Q $c1 "SELECT Walk AS walk, WithInserts AS with_inserts, ProductN AS product_n, Pages AS pages, RowsReturned AS rows_returned, RowsExpected AS rows_expected, Duplicates AS duplicates, Missing AS missing, Inversions AS inversions, LogicalReads AS logical_reads, ElapsedMs AS elapsed_ms, LateRowsInserted AS late_rows_inserted, LateRowsSeen AS late_rows_seen FROM dbo._CursorWalk ORDER BY Id") @{ variant = $Variant; tier = $tier }
    Csv 'cursor-index' (Invoke-Q $c1 "SELECT Idx AS idx, BuildMs AS build_ms, UsedMb AS used_mb, Rows AS rows FROM dbo._ExtraIndex ORDER BY Id") @{ variant = $Variant; tier = $tier }
    Drop-Db $c1

    # --- copy 2: rebuild everything (the compacted floor) and measure again
    Log "=== tier $tier : restore copy 2 (rebuilt)"
    $c2 = "${db}_c2"
    [void](Restore-Copy $c2 $bak $db)
    $rb = foreach ($t in 'AuditLogs', 'StockLedgerEntry', 'StockDocumentLine', 'StockDocument', 'Product', 'ProductBarcode', 'StockBalance') {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $ok = $true
        try { Invoke-NonQ $c2 "ALTER INDEX ALL ON dbo.$t REBUILD" } catch { $ok = $false; Log "  rebuild of $t failed: $($_.Exception.Message)" }
        [pscustomobject]@{ variant = $Variant; tier = $tier; tbl = $t; rebuild_ms = $(if ($ok) { $sw.ElapsedMilliseconds } else { -1 }) }
    }
    $rb | Export-Csv -Path (Join-Path $Results 'rebuild.csv') -NoTypeInformation -Append -Encoding ASCII
    Update-AllStats $c2
    Invoke-NonQ $c2 "CHECKPOINT"
    Context 'measure-rebuilt' $tier
    Measure-State $c2 $tier 'rebuilt'
    Drop-Db $c2
    [System.IO.File]::Delete($bak)
    Log "=== tier $tier done"
  } catch {
    # e.g. the 10 GB data limit of the Express engine: keep what was measured, record the failure, stop this variant
    $msg = ($_.Exception.Message -replace '[\r\n]+', ' ')
    Log "FAILED at tier ${tier}: $msg"
    [pscustomobject]@{ variant = $Variant; tier = $tier; at_utc = (Get-Date).ToUniversalTime().ToString('s'); message = $msg.Substring(0, [math]::Min(400, $msg.Length)) } | Export-Csv -Path (Join-Path $Results 'failures.csv') -NoTypeInformation -Append -Encoding ASCII
    foreach ($x in "${db}_c1", "${db}_c2") { try { Drop-Db $x } catch { } }
    break
  }
}
if (-not $KeepDb) { Drop-Db $db; Log "dropped $db" }
Log "finished"


