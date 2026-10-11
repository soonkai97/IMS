# lib.ps1 -- helpers shared by the drivers. Dot-source it: . .\lib.ps1
# Server: the LocalDB instance ImsDev (never the default MSSQLLocalDB instance). Scratch databases are named Ims_PerfGuid_<Variant>[_suffix].
$script:Server = '(localdb)\ImsDev'
$script:Here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $script:DataDir) { $script:DataDir = Join-Path $env:TEMP 'ims_guidpk' }
New-Item -ItemType Directory -Force -Path $script:DataDir | Out-Null

function Get-Conn([string]$db = 'master') {
    $c = New-Object System.Data.SqlClient.SqlConnection("Server=$script:Server;Database=$db;Integrated Security=true;Connect Timeout=60")
    $c.Open(); $c
}
function Invoke-Q([string]$db, [string]$sql, [int]$timeout = 3600) {
    $c = Get-Conn $db
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = $timeout
        $da = New-Object System.Data.SqlClient.SqlDataAdapter($cmd); $dt = New-Object System.Data.DataTable
        [void]$da.Fill($dt); ,$dt
    } finally { $c.Close() }
}
function Invoke-NonQ([string]$db, [string]$sql, [int]$timeout = 3600) {
    $c = Get-Conn $db
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = $timeout; [void]$cmd.ExecuteNonQuery() } finally { $c.Close() }
}
# Runs script files (in order) through sqlcmd with the variant's sqlcmd variables loaded. Extra variables as a hashtable.
function Invoke-SqlFiles([string]$variant, [string]$db, [string[]]$files, [hashtable]$vars = @{}, [string]$out = $null) {
    $wrapper = Join-Path $env:TEMP ("guidpk_wrap_{0}_{1}.sqlcmd" -f $variant, [guid]::NewGuid().ToString('N'))
    $lines = @()
    $lines += ":setvar Db $db"
    $lines += ":setvar DataDir `"$script:DataDir`""
    foreach ($k in $vars.Keys) { $lines += ":setvar $k `"$($vars[$k])`"" }
    $lines += ":r `"$(Join-Path $script:Here "variants\$variant.sqlcmd")`""
    foreach ($f in $files) { $lines += ":r `"$(Join-Path $script:Here $f)`"" }
    Set-Content -Path $wrapper -Value $lines -Encoding ASCII
    $args = @('-S', $script:Server, '-E', '-b', '-W', '-I', '-i', $wrapper)
    if ($db -and $db -ne 'master') { $args += @('-d', $db) }
    if ($out) { $args += @('-o', $out) }
    $r = & sqlcmd @args
    $code = $LASTEXITCODE
    [System.IO.File]::Delete($wrapper)
    if ($code -ne 0) { throw "sqlcmd failed ($code) for $($files -join ',')`n$($r -join "`n")" }
    $r
}
function Export-Table($dt, [string]$path, [hashtable]$extra = @{}) {
    $rows = foreach ($r in $dt.Rows) {
        $o = [ordered]@{}
        foreach ($k in $extra.Keys) { $o[$k] = $extra[$k] }
        foreach ($c in $dt.Columns) { $v = $r[$c.ColumnName]; $o[$c.ColumnName] = $(if ($v -is [System.DBNull]) { $null } else { $v }) }
        [pscustomobject]$o
    }
    if (Test-Path $path) { $rows | Export-Csv -Path $path -NoTypeInformation -Append -Encoding ASCII } else { $rows | Export-Csv -Path $path -NoTypeInformation -Encoding ASCII }
}
function Drop-Db([string]$db) {
    if ($db -notlike 'Ims_PerfGuid*') { throw "refusing to drop $db" }
    Invoke-NonQ 'master' "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END"
}
function Get-CpuContext {
    # total CPU % and the share used by sqlservr, 3 s sample: how busy the machine is with other work
    $s = (Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 3).CounterSamples | ForEach-Object CookedValue
    $total = [math]::Round(($s | Measure-Object -Average).Average, 1)
    [pscustomobject]@{ cpu_total_pct = $total }
}

# Starts $writers sqlcmd sessions that each run $txEach posting transactions; waits for all. Returns tx/s measured from the first start to the last end (server clock).
function Invoke-Writers([string]$db, [string]$tag, [int]$writers, [int]$txEach, [double]$spacing = 186.6, [string]$epoch = '2024-01-01', [int]$products = 50000) {
    $ordBase = [int64](Invoke-Q $db "SELECT v FROM dbo._State WHERE k='ordNext'").Rows[0].v
    $procs = @(); $outs = @()
    for ($w = 0; $w -lt $writers; $w++) {
        $q = "EXEC dbo._RunWriter '$tag', $w, $txEach, $writers, $ordBase, '$epoch', $spacing, $products"
        $o = Join-Path $env:TEMP ("guidpk_w{0}_{1}.txt" -f $w, [guid]::NewGuid().ToString('N')); $outs += $o
        $procs += Start-Process sqlcmd -ArgumentList @('-S', "`"$script:Server`"", '-E', '-b', '-I', '-d', $db, '-Q', "`"$q`"") -PassThru -WindowStyle Hidden -RedirectStandardOutput $o
    }
    $procs | Wait-Process
    $bad = @($procs | Where-Object { $_.ExitCode -ne 0 })
    $msg = ''
    if ($bad.Count) { $msg = ($outs | ForEach-Object { Get-Content $_ -ErrorAction SilentlyContinue } | Select-Object -First 10) -join "`n" }
    foreach ($o in $outs) { if (Test-Path -LiteralPath $o) { [System.IO.File]::Delete($o) } }
    if ($bad.Count) { throw "$($bad.Count) writers failed:`n$msg" }
    Invoke-NonQ $db ("UPDATE dbo._State SET v = {0} WHERE k='ordNext'" -f ($ordBase + [int64]($txEach + 1) * $writers))
    $r = (Invoke-Q $db "SELECT COUNT(*) n, SUM(Tx) tx, SUM(Retries) retries, DATEDIFF_BIG(MILLISECOND, MIN(StartUtc), MAX(EndUtc)) ms FROM dbo._WriterLog WHERE Tag='$tag'").Rows[0]
    [pscustomobject]@{ tag = $tag; writers = [int]$r.n; tx = [int]$r.tx; retries = [int]$r.retries; ms = [int64]$r.ms; tx_per_s = [math]::Round($r.tx / ($r.ms / 1000.0), 1) }
}
