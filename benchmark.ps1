$ErrorActionPreference = "Stop"
$Strategy = "round-robin"
$Requests = 5000
$Concurrency = 50
$WarmupRequests = 200
$TimeoutMilliseconds = 5000
$KeepAlive = $false

for ($index = 0; $index -lt $args.Count; $index++) {
    $argument = $args[$index]
    if ($argument -match "^(--strategy|-Strategy)$") {
        if (++$index -ge $args.Count) {
            throw "The $argument option requires a value."
        }

        $Strategy = $args[$index]
    }
    elseif ($argument -match "^--strategy=(.+)$") {
        $Strategy = $Matches[1]
    }
    elseif ($argument -match "^-Requests$") {
        $Requests = [int]$args[++$index]
    }
    elseif ($argument -match "^-Concurrency$") {
        $Concurrency = [int]$args[++$index]
    }
    elseif ($argument -match "^-WarmupRequests$") {
        $WarmupRequests = [int]$args[++$index]
    }
    elseif ($argument -match "^-TimeoutMilliseconds$") {
        $TimeoutMilliseconds = [int]$args[++$index]
    }
    elseif ($argument -match "^-KeepAlive$" -or $argument -eq "--keep-alive") {
        $KeepAlive = $true
    }
    else {
        throw "Unknown option '$argument'."
    }
}

$validStrategies = @(
    "round-robin",
    "weighted-round-robin",
    "random",
    "least-connections"
)
if ($Strategy -notin $validStrategies) {
    throw "Strategy '$Strategy' is invalid. Choose one of: $($validStrategies -join ', ')."
}

$root = $PSScriptRoot
$temp = Join-Path $env:TEMP ("endjinx-benchmark-" + [guid]::NewGuid().ToString("N"))
$configPath = Join-Path $temp "load-balancer.yaml"
$processes = New-Object System.Collections.Generic.List[object]

$requestWorker = {
    param($hostName, $port, $timeoutMilliseconds, $keepAlive)

    $client = New-Object System.Net.Sockets.TcpClient
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $stream = $null

    try {
        $connectTask = $client.ConnectAsync($hostName, $port)
        if (-not $connectTask.Wait($timeoutMilliseconds)) {
            throw "connect timeout"
        }

        $stream = $client.GetStream()
        $stream.ReadTimeout = $timeoutMilliseconds
        $stream.WriteTimeout = $timeoutMilliseconds

        $connection = if ($keepAlive) { "keep-alive" } else { "close" }
        $request = "GET / HTTP/1.1`r`nHost: benchmark`r`nConnection: $connection`r`n`r`n"
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

        if ($response.ToString() -notmatch "^HTTP/1\.1 200 OK") {
            throw "unexpected response"
        }

        [pscustomobject]@{
            Success = $true
            Milliseconds = $watch.Elapsed.TotalMilliseconds
            Error = $null
        }
    }
    catch {
        [pscustomobject]@{
            Success = $false
            Milliseconds = $watch.Elapsed.TotalMilliseconds
            Error = $_.Exception.Message
        }
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }

        $client.Dispose()
    }
}

function Wait-ForPort {
    param([int]$Port)

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $connectTask = $client.ConnectAsync("127.0.0.1", $Port)
            if ($connectTask.Wait(250) -and $client.Connected) {
                $client.Dispose()
                return
            }
        }
        catch {
        }

        $client.Dispose()
        Start-Sleep -Milliseconds 100
    }

    throw "Timed out waiting for port $Port."
}

function Start-EndJinxProcess {
    param(
        [string]$Project,
        [string[]]$Arguments,
        [string]$Name
    )

    $outputPath = Join-Path $temp "$Name.out.log"
    $errorPath = Join-Path $temp "$Name.err.log"
    $process = Start-Process `
        -FilePath "dotnet" `
        -WorkingDirectory $root `
        -ArgumentList (@("run", "--project", $Project, "--configuration", "Release", "--no-build", "--") + $Arguments) `
        -RedirectStandardOutput $outputPath `
        -RedirectStandardError $errorPath `
        -PassThru

    [void]$processes.Add($process)
    return $process
}

function Invoke-BenchmarkBatch {
    param(
        [int]$RequestCount,
        [int]$ConcurrencyLevel
    )

    $pool = [runspacefactory]::CreateRunspacePool(1, $ConcurrencyLevel)
    $pool.Open()
    $handles = New-Object System.Collections.Generic.List[object]
    $results = New-Object System.Collections.Generic.List[object]
    $wallClock = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        for ($index = 0; $index -lt $RequestCount; $index++) {
            while ($handles.Count -ge $ConcurrencyLevel) {
                foreach ($handle in $handles.ToArray()) {
                    if ($handle.AsyncResult.IsCompleted) {
                        $completed = $handle.PowerShell.EndInvoke($handle.AsyncResult)
                        $handle.PowerShell.Dispose()
                        [void]$handles.Remove($handle)
                        foreach ($result in $completed) {
                            [void]$results.Add($result)
                        }
                    }
                }

                if ($handles.Count -ge $ConcurrencyLevel) {
                    Start-Sleep -Milliseconds 1
                }
            }

            $powerShell = [powershell]::Create()
            $powerShell.RunspacePool = $pool
            [void]$powerShell.AddScript($requestWorker).
                AddArgument("127.0.0.1").
                AddArgument(9300).
                AddArgument($TimeoutMilliseconds).
                AddArgument($KeepAlive.IsPresent)
            $asyncResult = $powerShell.BeginInvoke()
            [void]$handles.Add([pscustomobject]@{
                PowerShell = $powerShell
                AsyncResult = $asyncResult
            })
        }

        while ($handles.Count -gt 0) {
            foreach ($handle in $handles.ToArray()) {
                if ($handle.AsyncResult.IsCompleted) {
                    $completed = $handle.PowerShell.EndInvoke($handle.AsyncResult)
                    $handle.PowerShell.Dispose()
                    [void]$handles.Remove($handle)
                    foreach ($result in $completed) {
                        [void]$results.Add($result)
                    }
                }
            }

            if ($handles.Count -gt 0) {
                Start-Sleep -Milliseconds 1
            }
        }
    }
    finally {
        $pool.Close()
        $pool.Dispose()
    }

    $wallClock.Stop()
    $successes = @($results | Where-Object Success)
    $latencies = @($successes | ForEach-Object { [double]$_.Milliseconds } | Sort-Object)
    $p50Index = [math]::Max(0, [math]::Ceiling($latencies.Count * 0.50) - 1)
    $p99Index = [math]::Max(0, [math]::Ceiling($latencies.Count * 0.99) - 1)
    $errors = @($results |
        Where-Object { -not $_.Success } |
        Group-Object Error |
        Sort-Object Count -Descending)

    return [pscustomobject]@{
        Total = $results.Count
        Success = $successes.Count
        Failures = $results.Count - $successes.Count
        Seconds = $wallClock.Elapsed.TotalSeconds
        RequestsPerSecond = if ($wallClock.Elapsed.TotalSeconds -gt 0) {
            $successes.Count / $wallClock.Elapsed.TotalSeconds
        }
        else {
            0
        }
        P50Milliseconds = if ($latencies.Count -gt 0) { $latencies[$p50Index] } else { [double]::NaN }
        P99Milliseconds = if ($latencies.Count -gt 0) { $latencies[$p99Index] } else { [double]::NaN }
        Errors = (($errors | ForEach-Object { "$($_.Count)x $($_.Name)" }) -join "; ")
    }
}

try {
    New-Item -ItemType Directory -Path $temp | Out-Null

    $yaml = @(
        "mode: l7"
        "listen:"
        "  host: 127.0.0.1"
        "  port: 9300"
        "strategy: $Strategy"
        "proxy:"
        "  connectionTimeoutMilliseconds: $TimeoutMilliseconds"
        "  timeoutMilliseconds: $TimeoutMilliseconds"
        "  maxRetries: 0"
        "healthCheck:"
        "  intervalSeconds: 60"
        "  timeoutMilliseconds: 1000"
        "pools:"
        "  api:"
        "    - host: 127.0.0.1"
        "      port: 8300"
        "      weight: 1"
        "    - host: 127.0.0.1"
        "      port: 8301"
        "      weight: 1"
        "routes:"
        "  - pathPrefix: /"
        "    pool: api"
    )
    $yaml -join "`n" | Set-Content -Path $configPath -Encoding ASCII

    Start-EndJinxProcess "EndJinx.csproj" @("8300") "backend-8300" | Out-Null
    Start-EndJinxProcess "EndJinx.csproj" @("8301") "backend-8301" | Out-Null
    Wait-ForPort 8300
    Wait-ForPort 8301

    Start-EndJinxProcess `
        "src\EndJinx.LoadBalancer\EndJinx.LoadBalancer.csproj" `
        @("--config", $configPath) `
        "load-balancer" | Out-Null
    Wait-ForPort 9300
    Start-Sleep -Milliseconds 500

    Write-Host "Strategy: $Strategy"
    Write-Host "Requests: $Requests"
    Write-Host "Concurrency: $Concurrency"
    Write-Host "Warm-up requests: $WarmupRequests"
    Write-Host "Connection: $(if ($KeepAlive) { 'keep-alive' } else { 'close' })"
    Write-Host ""

    $null = Invoke-BenchmarkBatch $WarmupRequests $Concurrency
    $result = Invoke-BenchmarkBatch $Requests $Concurrency

    [pscustomobject]@{
        Strategy = $Strategy
        RequestsPerSecond = [math]::Round($result.RequestsPerSecond, 2)
        P50Milliseconds = [math]::Round($result.P50Milliseconds, 2)
        P99Milliseconds = [math]::Round($result.P99Milliseconds, 2)
        Success = $result.Success
        Failures = $result.Failures
        WallSeconds = [math]::Round($result.Seconds, 2)
        FailureDetails = $result.Errors
    } | Format-List
}
finally {
    foreach ($process in $processes.ToArray()) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }

    if (Test-Path $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
