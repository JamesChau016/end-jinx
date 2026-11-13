<div align="center">

# EndJinx

### A hands-on C# networking project: an HTTP/1.1 server and configurable load balancer built on TCP sockets.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-networking-239120?logo=csharp&logoColor=white)
![TCP](https://img.shields.io/badge/TCP-proxying-244c5a)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[Overview](#overview) | [Features](#features) | [Quick start](#quick-start) | [How it works](#how-it-works) | [Testing](#testing) | [License](#license)

</div>

## Overview

EndJinx contains two small applications designed to make the work behind web servers and proxies easier to explore. It handles HTTP framing, connection lifecycle, concurrency, backend health, and load distribution directly, without Kestrel, ASP.NET Core, `HttpListener`, or a proxy framework.

| Application | What it does | Default port |
| --- | --- | ---: |
| HTTP server | Serves `GET /` and echoes `POST /echo` | `8000` |
| Load balancer | Forwards TCP traffic or routes HTTP paths to backend pools | `9000` |

## Quick start

**Requirements:** .NET SDK 10 or later. `curl` is useful for manual requests; PowerShell 5.1 or later is needed for the PowerShell scripts.

Build and run the HTTP server:

```powershell
dotnet build EndJinx.csproj
dotnet run --project EndJinx.csproj
```

In another terminal, try the available routes:

```powershell
curl.exe -i http://127.0.0.1:8000/
curl.exe -i -X POST http://127.0.0.1:8000/echo -H "Content-Type: text/plain" --data "hello from EndJinx"
```

Run the automated tests with:

```powershell
dotnet test EndJinx.Tests/EndJinx.Tests.csproj
```

## Features

### HTTP server

- Concurrent TCP clients and HTTP/1.1 keep-alive, including multiple requests on one connection.
- Request headers and `Content-Length` bodies, with five-second read timeouts, an 8 KiB header limit, and a 1 MiB body limit.
- Simple routing and responses for common errors (`400`, `404`, `413`, and `500`).

TLS, static files, chunked transfer encoding, and broader HTTP method support are outside the current scope.

## How it works

### Load balancer

The load balancer supports transparent Layer 4 TCP proxying and Layer 7 HTTP path routing. It checks backend health every five seconds, marks backends unhealthy after connection failures, retries failed backend attempts, and records per-backend connection and request metrics.

Choose a backend selection strategy in `config/load-balancer.yaml`:

| Strategy | Behavior |
| --- | --- |
| `round-robin` | Rotates across healthy backends |
| `weighted-round-robin` | Distributes traffic according to each backend's positive `weight` |
| `random` | Picks a healthy backend at random |
| `least-connections` | Picks the backend with the fewest active proxy connections |

Weighted example:

```yaml
strategy: weighted-round-robin
pools:
  default:
    - host: 127.0.0.1
      port: 8000
      weight: 5
    - host: 127.0.0.1
      port: 8001
      weight: 1
```

With these weights, backend `8000` receives about five requests for every request sent to `8001`. The smooth algorithm spreads selections over time instead of sending traffic in one large burst. Weights must be positive for this strategy.

The default configuration is `config/load-balancer.yaml`. Start the balancer with `dotnet run --project src/EndJinx.LoadBalancer`; pass `--config <path>` to load another YAML file. Layer 4 mode uses one pool. Layer 7 mode selects a named pool using the first matching `pathPrefix` and currently handles one request per connection, closing the backend connection after its response.

## Try the load balancer

Open three terminals and start two backend servers plus the balancer:

```powershell
# Terminal 1
dotnet run --project EndJinx.csproj -- 8000

# Terminal 2
dotnet run --project EndJinx.csproj -- 8001

# Terminal 3
dotnet run --project src/EndJinx.LoadBalancer
```

The default YAML sends traffic from port `9000` to ports `8000` and `8001`. In a fourth terminal, make several requests and watch the balancer log its routing decisions:

```powershell
1..6 | ForEach-Object { curl.exe -s -i http://127.0.0.1:9000/ }
```

Stop one backend to observe health checks remove it from rotation, then restart it to see it re-admitted. Stop the applications with `Ctrl+C`.

## Testing

Build the load balancer independently with `dotnet build src/EndJinx.LoadBalancer/EndJinx.LoadBalancer.csproj`. To run only pool and strategy tests:

```powershell
dotnet test EndJinx.Tests/EndJinx.Tests.csproj --filter FullyQualifiedName~PoolSelectorTests
```

## Load testing

Run the HTTP load test from the repository root:

```powershell
.\load-test.ps1 -ConcurrencyLevels @(10, 50, 100) -RequestsPerLevel 500
```

Or use the Bash script with `./load-test.sh -l 10,50,100 -n 500`. Both scripts report successes, failures, average latency, and throughput. `benchmark.ps1` runs a separate load-balancer benchmark. Choose one strategy per run:

```powershell
.\benchmark.ps1 --strategy round-robin
.\benchmark.ps1 --strategy weighted-round-robin
.\benchmark.ps1 --strategy random
.\benchmark.ps1 --strategy least-connections
```

The benchmark reports throughput, p50/p99 latency, successes, and failures. Adjust it with `-Requests`, `-Concurrency`, `-WarmupRequests`, or `--keep-alive`.

## Repository map

```text
./                         HTTP server project and shared server code
src/EndJinx.LoadBalancer/  Load balancer application
EndJinx.Tests/             Unit and integration tests (xUnit)
config/                    Example load-balancer YAML configuration
BackendApis/               Supporting backend API sample
```

See [AGENTS.md](AGENTS.md) for contributor conventions. Configuration examples live in `config/`; benchmark and load-test scripts are in the repository root.

## License

EndJinx is available under the [MIT License](LICENSE).
