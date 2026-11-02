$ErrorActionPreference = 'Stop'
$cfg = Get-Content (Join-Path $PSScriptRoot '..\..\dist\windows\config.json') | ConvertFrom-Json
$seconds = if ($args[0]) { [int]$args[0] } else { 8 }
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.ConnectAsync("ws://127.0.0.1:$($cfg.Port)/ws", [Threading.CancellationToken]::None).Wait()
$buf = New-Object byte[] 8192
$end = (Get-Date).AddSeconds($seconds)
while ((Get-Date) -lt $end) {
    $cts = New-Object Threading.CancellationTokenSource 3000
    try {
        $r = $ws.ReceiveAsync($buf, $cts.Token).GetAwaiter().GetResult()
        [Text.Encoding]::UTF8.GetString($buf, 0, $r.Count)
    } catch { }
}
