<#
  22-interleaved-ab.ps1 -- latency of two API builds against ONE database, called alternately, endpoint by endpoint.

  A shared laptop changes speed by 2-5x within minutes, so two sequential runs of 02-measure-api.ps1 cannot be compared to better than a factor of two.
  Here each endpoint is called -Runs times on each build, alternately (old, new, old, new ...), so both sides see the same machine state; the comparison
  is the ratio of their medians and 95th percentiles. The URLs (with the ids and cursors each build needs) are taken from the Url column of one
  02-measure-api.ps1 result file per build, matched by endpoint name.

    powershell -File docs/dba/22-interleaved-ab.ps1 -OldCsv docs/dba/results-a.csv -NewCsv docs/dba/results-b.csv -OldUrl http://localhost:5612 -NewUrl http://localhost:5611 -Label 1m-ab
  Output: docs/dba/results-<Label>.csv. -Only is a regex on the endpoint name; exports run -ExportRuns times.
#>
param(
    [Parameter(Mandatory)][string]$OldCsv, [Parameter(Mandatory)][string]$NewCsv,
    [Parameter(Mandatory)][string]$OldUrl, [Parameter(Mandatory)][string]$NewUrl,
    [string]$Label = 'ab', [int]$Runs = 20, [int]$ExportRuns = 3, [string]$Only = '.',
    [string]$User = $(if ($env:IMS_PERF_USER) { $env:IMS_PERF_USER } else { 'admin' }),
    [string]$Password = $env:IMS_PERF_PASSWORD
)
if (-not $Password) { $Password = (Get-Content (Join-Path $PSScriptRoot '..\..\src\Ims.Api\appsettings.Development.json') -Raw | ConvertFrom-Json).Seed.AdminPassword }
Add-Type -AssemblyName System.Net.Http
function New-Client([string]$base) {
    $h = New-Object System.Net.Http.HttpClientHandler; $h.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
    $c = New-Object System.Net.Http.HttpClient($h); $c.Timeout = [TimeSpan]::FromSeconds(125)
    $b = New-Object System.Net.Http.StringContent((@{ username = $User; password = $Password } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json')
    $j = $c.PostAsync("$base/api/auth/login", $b).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $c.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $j.accessToken); $c
}
$old = New-Client $OldUrl; $new = New-Client $NewUrl
function Time-Get($client, [string]$url) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); $r = $client.GetAsync($url).GetAwaiter().GetResult(); [void]$r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(); $sw.Stop()
    [pscustomobject]@{ Status = [int]$r.StatusCode; Ms = $sw.Elapsed.TotalMilliseconds }
}
function Get-Rank([double[]]$v, [double]$p) { $s = $v | Sort-Object; $i = [math]::Ceiling($p * $s.Count) - 1; if ($i -lt 0) { $i = 0 }; $s[$i] }
$newByName = @{}; foreach ($r in (Import-Csv $NewCsv)) { $newByName[$r.Endpoint] = $r }
$rows = @()
foreach ($o in (Import-Csv $OldCsv)) {
    if ($o.Endpoint -notmatch $Only -or -not $newByName.ContainsKey($o.Endpoint)) { continue }
    $uo = $OldUrl + [uri]::EscapeUriString($o.Url); $un = $NewUrl + [uri]::EscapeUriString($newByName[$o.Endpoint].Url)
    $n = if ($o.Endpoint -like 'export:*') { $ExportRuns } else { $Runs }
    [void](Time-Get $old $uo); [void](Time-Get $new $un)
    $to = @(); $tn = @(); $so = 0; $sn = 0
    for ($i = 0; $i -lt $n; $i++) {
        if ($i % 2 -eq 0) { $a = Time-Get $old $uo; $b = Time-Get $new $un } else { $b = Time-Get $new $un; $a = Time-Get $old $uo }
        $to += $a.Ms; $tn += $b.Ms; $so = $a.Status; $sn = $b.Status
    }
    $row = [pscustomobject]@{ Label = $Label; Endpoint = $o.Endpoint; StatusOld = $so; StatusNew = $sn; Runs = $n
        P50Old = [math]::Round((Get-Rank $to 0.5), 1); P50New = [math]::Round((Get-Rank $tn 0.5), 1)
        P95Old = [math]::Round((Get-Rank $to 0.95), 1); P95New = [math]::Round((Get-Rank $tn 0.95), 1) }
    $rows += $row
    '{0,-46} {1}/{2}  p50 {3,8} -> {4,8}   p95 {5,8} -> {6,8}' -f $row.Endpoint, $so, $sn, $row.P50Old, $row.P50New, $row.P95Old, $row.P95New
}
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $PSScriptRoot "results-$Label.csv")
