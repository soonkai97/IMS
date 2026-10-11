<#
  90-summarize.ps1 -- turns the raw result files of run-variant.ps1 into the summary tables used by README.md (printed as markdown and written
  as summary-*.csv next to them). Ratios are against variant C (the identity baseline) at the same tier and state.
    powershell -File 90-summarize.ps1 [-Results .\results]
#>
param([string]$Results = (Join-Path $PSScriptRoot 'results'))
$ErrorActionPreference = 'Stop'
$order = 'C', 'B1', 'B2', 'A1', 'A2', 'A3'
function Load($n) { $p = Join-Path $Results "$n.csv"; if (Test-Path $p) { Import-Csv $p } else { @() } }
function Med($xs) { $a = @($xs | Where-Object { $_ -ne $null -and $_ -ne '' } | ForEach-Object { [double]$_ } | Sort-Object); if ($a.Count -eq 0) { return $null }; $m = [int][math]::Floor($a.Count / 2); if ($a.Count % 2) { $a[$m] } else { ($a[$m - 1] + $a[$m]) / 2 } }
function Fmt($x, $d = 1) { if ($x -eq $null) { '' } else { ([double]$x).ToString("N$d", [Globalization.CultureInfo]::InvariantCulture) } }
function Out-Csv($name, $rows) { $rows | Export-Csv -Path (Join-Path $Results "summary-$name.csv") -NoTypeInformation -Encoding ASCII }
function MdTable($rows, $cols) {
    ('| ' + ($cols -join ' | ') + ' |'); ('|' + (($cols | ForEach-Object { '---' }) -join '|') + '|')
    foreach ($r in $rows) { '| ' + (($cols | ForEach-Object { $r.$_ }) -join ' | ') + ' |' }
}

# ---- sizes
$sizes = Load 'sizes'
$rows = foreach ($g in $sizes | Group-Object variant, tier, state) {
    $v = $g.Group[0]
    $tbl = $g.Group | Group-Object tbl
    $o = [ordered]@{ variant = $v.variant; tier = $v.tier; state = $v.state }
    $total = 0.0
    foreach ($t in 'StockLedgerEntry', 'StockDocument', 'StockDocumentLine', 'Product', 'StockBalance', 'AuditLogs') {
        $tg = $g.Group | Where-Object { $_.tbl -eq $t }
        $mb = ($tg | Measure-Object used_mb -Sum).Sum
        $clu = ($tg | Where-Object { $_.kind -eq 'CLUSTERED' })
        $rowsN = [double]($clu.rows | Select-Object -First 1)
        $o["$t`_mb"] = [math]::Round($mb, 1)
        $o["$t`_bytes_per_row"] = if ($rowsN -gt 0) { [math]::Round($mb * 1MB / $rowsN, 0) } else { $null }
        $o["$t`_clustered_mb"] = [math]::Round(($clu | Measure-Object used_mb -Sum).Sum, 1)
        $total += $mb
    }
    $others = ($g.Group | Where-Object { $_.tbl -notin 'StockLedgerEntry', 'StockDocument', 'StockDocumentLine', 'Product', 'StockBalance', 'AuditLogs' } | Measure-Object used_mb -Sum).Sum
    $o['hot_tables_mb'] = [math]::Round($total, 1)
    $o['all_tables_mb'] = [math]::Round($total + $others, 1)
    $o['fk_index_mb'] = [math]::Round(($g.Group | Where-Object { $_.is_fk_index -eq 'True' } | Measure-Object used_mb -Sum).Sum, 1)
    $o['public_id_index_mb'] = [math]::Round(($g.Group | Where-Object { $_.is_public_id_index -eq 'True' } | Measure-Object used_mb -Sum).Sum, 1)
    [pscustomobject]$o
}
$rows = $rows | Sort-Object tier, state, { $order.IndexOf($_.variant) }
foreach ($r in $rows) {
    $base = $rows | Where-Object { $_.variant -eq 'C' -and $_.tier -eq $r.tier -and $_.state -eq $r.state }
    $r | Add-Member -NotePropertyName 'multiple_of_C' -NotePropertyValue $(if ($base) { [math]::Round($r.hot_tables_mb / $base.hot_tables_mb, 2) } else { $null })
}
Out-Csv 'size' $rows
'## Size (MB, all indexes of the hot tables) and bytes per row'
MdTable $rows @('variant', 'tier', 'state', 'hot_tables_mb', 'multiple_of_C', 'StockLedgerEntry_mb', 'StockLedgerEntry_bytes_per_row', 'AuditLogs_mb', 'AuditLogs_bytes_per_row', 'StockDocument_mb', 'StockDocumentLine_mb', 'Product_mb', 'StockBalance_mb', 'fk_index_mb', 'public_id_index_mb')

# ---- fragmentation
$frag = Load 'frag'
$rows = foreach ($g in $frag | Group-Object variant, tier, state) {
    $v = $g.Group[0]
    $o = [ordered]@{ variant = $v.variant; tier = $v.tier; state = $v.state }
    foreach ($pair in @(@('StockLedgerEntry', 'PK_StockLedgerEntry'), @('AuditLogs', 'PK_AuditLogs'), @('StockDocumentLine', 'PK_StockDocumentLine'), @('StockDocument', 'PK_StockDocument'), @('Product', 'PK_Product'))) {
        $x = $g.Group | Where-Object { $_.tbl -eq $pair[0] -and $_.idx -eq $pair[1] }
        $key = $pair[0] -replace '^Stock', ''
        $o["$key`_pk_frag_pct"] = $x.frag_pct; $o["$key`_pk_density_pct"] = $x.density_pct
    }
    $big = $g.Group | Where-Object { [int]$_.page_count -ge 1000 }
    $w = ($big | Measure-Object { [double]$_.page_count } -Sum).Sum
    $o['all_indexes_weighted_frag_pct'] = if ($w) { [math]::Round((($big | ForEach-Object { [double]$_.frag_pct * [double]$_.page_count } | Measure-Object -Sum).Sum) / $w, 1) } else { $null }
    $o['all_indexes_weighted_density_pct'] = if ($w) { [math]::Round((($big | ForEach-Object { [double]$_.density_pct * [double]$_.page_count } | Measure-Object -Sum).Sum) / $w, 1) } else { $null }
    [pscustomobject]$o
}
$rows = $rows | Sort-Object tier, state, { $order.IndexOf($_.variant) }
Out-Csv 'fragmentation' $rows
''; '## Fragmentation and page density (leaf level, DETAILED)'
MdTable $rows @('variant', 'tier', 'state', 'LedgerEntry_pk_frag_pct', 'LedgerEntry_pk_density_pct', 'AuditLogs_pk_frag_pct', 'AuditLogs_pk_density_pct', 'DocumentLine_pk_frag_pct', 'DocumentLine_pk_density_pct', 'Document_pk_frag_pct', 'Document_pk_density_pct', 'all_indexes_weighted_frag_pct', 'all_indexes_weighted_density_pct')

# ---- throughput
$tp = Load 'throughput'
$rows = foreach ($g in $tp | Group-Object variant, tier) {
    $v = $g.Group[0]
    [pscustomobject][ordered]@{
        variant = $v.variant; tier = $v.tier; windows = $g.Count
        tx_per_s_median = [math]::Round((Med ($g.Group.tx_per_s)), 0); tx_per_s_best = [math]::Round(($g.Group | ForEach-Object { [double]$_.tx_per_s } | Measure-Object -Maximum).Maximum, 0)
        ledger_rows_per_s_median = [math]::Round((Med ($g.Group.ledger_rows_per_s)), 0)
        retries = ($g.Group | ForEach-Object { [double]$_.retries } | Measure-Object -Sum).Sum
        leaf_allocations_per_tx = [math]::Round((Med ($g.Group.leaf_allocations_per_tx)), 2); leaf_allocations_per_s = [math]::Round((Med ($g.Group.leaf_allocations_per_s)), 0)
        log_bytes_per_tx = [math]::Round((Med ($g.Group.log_bytes_per_tx)), 0)
        sql_cpu_ms_per_tx = [math]::Round((Med ($g.Group.sql_cpu_ms_per_tx)), 2)
        cpu_total_pct_before = [math]::Round((Med ($g.Group.cpu_total_pct_before)), 0)
    }
}
$rows = $rows | Sort-Object tier, { $order.IndexOf($_.variant) }
foreach ($r in $rows) {
    $base = $rows | Where-Object { $_.variant -eq 'C' -and $_.tier -eq $r.tier }
    $r | Add-Member -NotePropertyName 'ratio_to_C_median' -NotePropertyValue $(if ($base) { [math]::Round($r.tx_per_s_median / $base.tx_per_s_median, 2) } else { $null })
}
Out-Csv 'throughput' $rows
''; '## Posting throughput, 16 concurrent writers (3 windows of 16,000 transactions on a restored copy of the tier)'
MdTable $rows @('variant', 'tier', 'tx_per_s_median', 'tx_per_s_best', 'ratio_to_C_median', 'ledger_rows_per_s_median', 'retries', 'leaf_allocations_per_tx', 'leaf_allocations_per_s', 'log_bytes_per_tx', 'sql_cpu_ms_per_tx', 'cpu_total_pct_before')

# ---- queries: median over parameter sets of warm logical reads / warm mean ms, per query
$q = Load 'queries'
$rows = foreach ($g in $q | Group-Object variant, tier, state, q) {
    $v = $g.Group[0]
    [pscustomobject][ordered]@{
        variant = $v.variant; tier = $v.tier; state = $v.state; q = $v.q; psets = $g.Count
        warm_reads_median = Med ($g.Group.warm_logical_reads); warm_reads_max = ($g.Group | ForEach-Object { [double]$_.warm_logical_reads } | Measure-Object -Maximum).Maximum
        warm_ms_median = Med ($g.Group.warm_mean_ms); cold_physical_reads_median = Med ($g.Group.cold_physical_reads); cold_ms_median = Med ($g.Group.cold_ms)
    }
}
foreach ($r in $rows) {
    $base = $rows | Where-Object { $_.variant -eq 'C' -and $_.tier -eq $r.tier -and $_.state -eq $r.state -and $_.q -eq $r.q }
    $r | Add-Member -NotePropertyName 'reads_ratio_to_C' -NotePropertyValue $(if ($base -and $base.warm_reads_median) { [math]::Round($r.warm_reads_median / $base.warm_reads_median, 2) } else { $null })
    $r | Add-Member -NotePropertyName 'ms_ratio_to_C' -NotePropertyValue $(if ($base -and $base.warm_ms_median) { [math]::Round($r.warm_ms_median / $base.warm_ms_median, 2) } else { $null })
}
$rows = $rows | Sort-Object tier, state, q, { $order.IndexOf($_.variant) }
Out-Csv 'queries' $rows

# ---- cursor walks, backup/restore, buffer pool, rebuild, load
$cw = Load 'cursor-walk' | Where-Object { $_.product_n -eq '1' }   # the hottest product: the longest walk (the other two are in the raw file)
if ($cw) { $cwr = foreach ($g in $cw | Group-Object variant, tier, walk, with_inserts) { $v = $g.Group[0]; [pscustomobject][ordered]@{ variant = $v.variant; tier = $v.tier; walk = $v.walk; with_inserts = $v.with_inserts
    pages = ($g.Group | ForEach-Object { [double]$_.pages } | Measure-Object -Sum).Sum; reads_per_page = [math]::Round((($g.Group | ForEach-Object { [double]$_.logical_reads } | Measure-Object -Sum).Sum) / (($g.Group | ForEach-Object { [double]$_.pages } | Measure-Object -Sum).Sum), 1)
    ms_per_page = [math]::Round((($g.Group | ForEach-Object { [double]$_.elapsed_ms } | Measure-Object -Sum).Sum) / (($g.Group | ForEach-Object { [double]$_.pages } | Measure-Object -Sum).Sum), 2)
    duplicates = ($g.Group | ForEach-Object { [double]$_.duplicates } | Measure-Object -Sum).Sum; missing = ($g.Group | ForEach-Object { [double]$_.missing } | Measure-Object -Sum).Sum
    big_inversion_pct = [math]::Round(100 * (($g.Group | ForEach-Object { [double]$_.big_inversions } | Measure-Object -Sum).Sum) / (($g.Group | ForEach-Object { [double]$_.rows_returned } | Measure-Object -Sum).Sum), 1)
    inversion_pct = [math]::Round(100 * (($g.Group | ForEach-Object { [double]$_.inversions } | Measure-Object -Sum).Sum) / (($g.Group | ForEach-Object { [double]$_.rows_returned } | Measure-Object -Sum).Sum), 1)
    late_inserted = ($g.Group | ForEach-Object { [double]$_.late_rows_inserted } | Measure-Object -Sum).Sum; late_seen = ($g.Group | ForEach-Object { [double]$_.late_rows_seen } | Measure-Object -Sum).Sum } }
  $cwr = $cwr | Sort-Object tier, walk, with_inserts, { $order.IndexOf($_.variant) }
  Out-Csv 'cursor-walk' $cwr
  ''; '## Cursor walks (3 products per walk)'; MdTable $cwr @('variant', 'tier', 'walk', 'with_inserts', 'pages', 'reads_per_page', 'ms_per_page', 'duplicates', 'missing', 'inversion_pct', 'big_inversion_pct', 'late_inserted', 'late_seen') }

$br = Load 'backup-restore' | Sort-Object tier, { $order.IndexOf($_.variant) }
if ($br) { ''; '## Backup and restore (uncompressed: Express)'; MdTable $br @('variant', 'tier', 'backup_mb', 'backup_ms', 'restore_ms', 'data_file_mb', 'allocated_mb') }

$bp = Load 'bufferpool' | Where-Object { $_.tbl -eq '(all)' } | Sort-Object tier, { $order.IndexOf($_.variant) }
if ($bp) { ''; '## Buffer pool after the shift workload (cold start, 4 readers x 1,500 reads + 4 writers x 2,500 postings)'; MdTable $bp @('variant', 'tier', 'cached_pages', 'cached_mb', 'dirty_pages') }

$rb = Load 'rebuild'
if ($rb) { $rbr = @(foreach ($g in $rb | Group-Object variant, tier) { $v = $g.Group[0]; [pscustomobject][ordered]@{ variant = $v.variant; tier = $v.tier; rebuild_all_s = [math]::Round((($g.Group | ForEach-Object { [double]$_.rebuild_ms } | Measure-Object -Sum).Sum) / 1000, 1) } }) | Sort-Object tier, { $order.IndexOf($_.variant) }
  Out-Csv 'rebuild' $rbr; ''; '## Rebuild of all indexes (offline)'; MdTable $rbr @('variant', 'tier', 'rebuild_all_s') }

$ld = Load 'load'
if ($ld) { $ldr = @(foreach ($g in $ld | Group-Object variant, tier) { $v = $g.Group[0]; $tx = ($g.Group | ForEach-Object { [double]$_.tx } | Measure-Object -Sum).Sum; $ms = ($g.Group | ForEach-Object { [double]$_.ms } | Measure-Object -Sum).Sum
    [pscustomobject][ordered]@{ variant = $v.variant; tier = $v.tier; tx = $tx; seconds = [math]::Round($ms / 1000, 0); tx_per_s = [math]::Round($tx / ($ms / 1000), 0); retries = ($g.Group | ForEach-Object { [double]$_.retries } | Measure-Object -Sum).Sum } }) | Sort-Object tier, { $order.IndexOf($_.variant) }
  Out-Csv 'load' $ldr; ''; '## Organic load (16 writers) from the previous tier to this one'; MdTable $ldr @('variant', 'tier', 'tx', 'seconds', 'tx_per_s', 'retries') }



$ss = Load 'splits-sample' | Sort-Object tier, { $order.IndexOf($_.variant) }
if ($ss) { Out-Csv 'splits-sample' $ss; ''; '## Mid-page splits (LOP_DELETE_SPLIT in the log) in a sample of 800 posting transactions'; MdTable $ss @('variant', 'tier', 'tx', 'mid_page_splits', 'mid_page_splits_per_tx', 'log_bytes') }
