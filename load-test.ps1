param(
    [string]$HostName = "127.0.0.1",
    [int]$Port = 8000,
    [int[]]$ConcurrencyLevels = @(1, 10, 25, 50, 100, 250),
    [int]$RequestsPerLevel = 100,
    [int]$TimeoutMilliseconds = 10000,
    [switch]$KeepConnectionsOpen
)

$worker = {
    param($HostName, $Port, $TimeoutMilliseconds, $KeepConnectionsOpen)

    $client = New-Object System.Net.Sockets.TcpClient
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $connectTask = $client.ConnectAsync($HostName, $Port)
        if (-not $connectTask.Wait($TimeoutMilliseconds)) {
            throw "connect timeout"
        }

        $stream = $client.GetStream()
        $stream.ReadTimeout = $TimeoutMilliseconds
        $stream.WriteTimeout = $TimeoutMilliseconds

        $connection = if ($KeepConnectionsOpen) { "keep-alive" } else { "close" }
        $request = "GET / HTTP/1.1`r`nHost: $HostName`r`nConnection: $connection`r`n`r`n"
        $requestBytes = [System.Text.Encoding]::ASCII.GetBytes($request)
        $stream.Write($requestBytes, 0, $requestBytes.Length)
        $stream.Flush()

        $buffer = New-Object byte[] 4096
        $response = New-Object System.Text.StringBuilder
        do {
            $read = $stream.Read($buffer, 0, $buffer.Length)
            if ($read -gt 0) {
                [void]$response.Append([System.Text.Encoding]::ASCII.GetString($buffer, 0, $read))
            }
        } while ($read -gt 0 -and $stream.DataAvailable)

        $text = $response.ToString()
        if ($text -notmatch "^HTTP/1\.1 200 OK") {
            throw "unexpected response: $($text.Split("`r`n")[0])"
        }

        [pscustomobject]@{
            Success = $true
            Error = $null
            Milliseconds = $stopwatch.ElapsedMilliseconds
        }
    }
    catch {
        [pscustomobject]@{
            Success = $false
            Error = $_.Exception.Message
            Milliseconds = $stopwatch.ElapsedMilliseconds
        }
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
        $client.Dispose()
    }
}

$pool = [runspacefactory]::CreateRunspacePool(1, [Environment]::ProcessorCount * 4)
$pool.Open()

try {
    Write-Host "Testing $HostName`:$Port"
    Write-Host "Requests per level: $RequestsPerLevel"
    Write-Host "Timeout: $TimeoutMilliseconds ms"
    Write-Host ""

    foreach ($level in $ConcurrencyLevels) {
        $handles = New-Object System.Collections.Generic.List[object]
        $results = New-Object System.Collections.Generic.List[object]
        $start = [System.Diagnostics.Stopwatch]::StartNew()

        for ($index = 0; $index -lt $RequestsPerLevel; $index++) {
            while ($handles.Count -ge $level) {
                $completed = $handles | Where-Object { $_.AsyncResult.IsCompleted }
                if ($completed.Count -eq 0) {
                    Start-Sleep -Milliseconds 10
                    continue
                }

                foreach ($handle in $completed) {
                    $result = $handle.PowerShell.EndInvoke($handle.AsyncResult)
                    $handle.PowerShell.Dispose()
                    [void]$handles.Remove($handle)
                    foreach ($item in $result) { [void]$results.Add($item) }
                }
            }

            $powershell = [powershell]::Create()
            $powershell.RunspacePool = $pool
            [void]$powershell.AddScript($worker)
            [void]$powershell.AddArgument($HostName)
            [void]$powershell.AddArgument($Port)
            [void]$powershell.AddArgument($TimeoutMilliseconds)
            [void]$powershell.AddArgument($KeepConnectionsOpen.IsPresent)
            $asyncResult = $powershell.BeginInvoke()
            $handles.Add([pscustomobject]@{
                PowerShell = $powershell
                AsyncResult = $asyncResult
            })
        }

        while ($handles.Count -gt 0) {
            foreach ($handle in @($handles)) {
                if ($handle.AsyncResult.IsCompleted) {
                    $result = $handle.PowerShell.EndInvoke($handle.AsyncResult)
                    $handle.PowerShell.Dispose()
                    [void]$handles.Remove($handle)
                    foreach ($item in $result) { [void]$results.Add($item) }
                }
            }
            if ($handles.Count -gt 0) { Start-Sleep -Milliseconds 10 }
        }

        $start.Stop()
        $successes = @($results | Where-Object Success).Count
        $failures = $results.Count - $successes
        $average = if ($results.Count -gt 0) {
            [math]::Round((($results | Measure-Object Milliseconds -Average).Average), 1)
        } else { 0 }
        $rate = if ($start.Elapsed.TotalSeconds -gt 0) {
            [math]::Round($results.Count / $start.Elapsed.TotalSeconds, 1)
        } else { 0 }

        Write-Host ("Concurrency {0,4}: success={1,4} failure={2,4} avg={3,7} ms rate={4,7}/s" -f $level, $successes, $failures, $average, $rate)

        if ($failures -gt 0) {
            $commonError = $results | Where-Object { -not $_.Success } | Group-Object Error | Sort-Object Count -Descending | Select-Object -First 1
            Write-Host "  Most common failure: $($commonError.Name) ($($commonError.Count))" -ForegroundColor Yellow
        }
    }
}
finally {
    $pool.Close()
    $pool.Dispose()
}
