# EndJinx

EndJinx is a learning project containing two small C# networking applications:

- A raw TCP HTTP/1.1 web server.
- A Layer 4 TCP load balancer that proxies connections to a pool of web-server
  instances.

The project focuses on HTTP framing, parsing, connection lifecycle,
concurrency, reverse-proxying, backend health, and load-balancing algorithms
without using Kestrel, ASP.NET Core, `HttpListener`, or a proxy framework.

## Requirements

- .NET SDK 10.0 or later
- `curl` for manual requests and load tests
- PowerShell 5.1 or later for `load-test.ps1`

## Features

### HTTP server

- Listens on `0.0.0.0:8000` by default; the port is configurable.
- Handles multiple TCP clients concurrently.
- Parses request headers and `Content-Length` request bodies.
- Supports HTTP/1.1 keep-alive and multiple requests per connection.
- Times out incomplete or idle reads after five seconds.
- Limits headers to 8 KiB and request bodies to 1 MiB.
- Returns `400`, `404`, `413`, and `500` responses for supported error cases.
- Provides `GET /` and `POST /echo` routes.

TLS, static files, chunked transfer encoding, and richer HTTP methods are not
implemented.

### Load balancer

- Listens on port `9000` by default.
- Proxies raw TCP bytes in both directions between clients and backends.
- Supports multiple backend ports supplied at startup.
- Performs active health checks every five seconds.
- Removes unhealthy backends and re-admits recovered backends.
- Marks backends unhealthy after passive connection failures.
- Tracks active TCP connections per backend.
- Supports three selection strategies:
  - `round-robin`: rotates through healthy backends.
  - `random`: chooses a healthy backend randomly.
  - `least-connections`: chooses the healthy backend with the fewest active
    proxy connections.

The load balancer supports both Layer 4 TCP proxying and Layer 7 HTTP path
routing. It loads listener, mode, pools, strategies, health checks, and routes
from `config/load-balancer.yaml` by default:

```powershell
dotnet run --project src/EndJinx.LoadBalancer
```

Use another configuration file with `--config`. L4 is transparent TCP routing
and requires one pool. L7 routes each HTTP connection by the first matching
`pathPrefix` and can use multiple named pools. The current L7 implementation
handles one request per connection and asks the backend to close after its
response.

## File structure

```text
EndJinx.csproj                         HTTP server project
Program.cs                             HTTP server listener and accept loop
HttpConnection.cs                      Per-client HTTP connection lifecycle
RequestParser.cs                       HTTP request framing and parsing
ResponseBuilder.cs                     HTTP response serialization
Router.cs                              HTTP method/path routing
Exceptions.cs                          HTTP exception types and status codes
Logger.cs                              Console logging abstraction

src/EndJinx.LoadBalancer/
  EndJinx.LoadBalancer.csproj          Load balancer project
  Program.cs                           YAML-driven listener and mode setup
  LoadBalancerConfiguration.cs         YAML model, loading, and validation
  PoolSelector.cs                      Backend pool and selection strategies
  TcpProxy.cs                          Bidirectional TCP proxy
  HttpProxy.cs                         Layer 7 HTTP path-routing proxy
  HealthCheck.cs                       Active backend health checks

config/
  load-balancer.yaml                   Default L4/L7 load-balancer settings

EndJinx.Tests/
  ServerTests.cs                       HTTP parser, router, and response tests
  ConnectionIntegrationTests.cs        HTTP connection integration tests
  LoadBalancerIntegrationTests.cs      TCP proxy integration tests
  PoolSelectorTests.cs                 Pool, health, and strategy tests

requests.http                          Sample HTTP requests for VS Code
load-test.ps1                          PowerShell load test
load-test.sh                           Bash and curl load test
LEARNING_PLAN.md                       HTTP server learning plan
LOAD_BALANCER_LEARNING_PLAN.md         Load balancer learning plan
```

## Build and automated tests

Run these commands from the repository root.

Build the HTTP server:

```powershell
dotnet build EndJinx.csproj
```

Build the load balancer:

```powershell
dotnet build src/EndJinx.LoadBalancer/EndJinx.LoadBalancer.csproj
```

Run all unit and integration tests:

```powershell
dotnet test EndJinx.Tests/EndJinx.Tests.csproj
```

Run only the pool and strategy tests:

```powershell
dotnet test EndJinx.Tests/EndJinx.Tests.csproj --filter FullyQualifiedName~PoolSelectorTests
```

## Manual test: HTTP server

Start the server in one terminal:

```powershell
dotnet run --project EndJinx.csproj
```

In another terminal, send requests:

```powershell
curl.exe -i http://127.0.0.1:8000/

curl.exe -i -X POST http://127.0.0.1:8000/echo `
  -H "Content-Type: text/plain" `
  --data "hello from EndJinx"

curl.exe -i http://127.0.0.1:8000/missing
```

Use `dotnet run --project EndJinx.csproj -- 8001` to run the server on another
port. The same examples are available in `requests.http`.

## Manual test: load balancer

Use four terminals for a complete local test.

1. Start two backend servers:

   ```powershell
   dotnet run --project EndJinx.csproj -- 8000
   dotnet run --project EndJinx.csproj -- 8001
   ```

2. Start the load balancer in a third terminal:

   ```powershell
   dotnet run --project src/EndJinx.LoadBalancer
   ```

The default YAML listens on port 9000 and uses backend ports 8000 and 8001.
Change `config/load-balancer.yaml` to customize the pool or strategy.

3. Send requests through the load balancer from the fourth terminal:

   ```powershell
   1..6 | ForEach-Object { curl.exe -s -i http://127.0.0.1:9000/ }
   ```

   Watch the load-balancer console to see which backend receives each TCP
   connection.

4. Try the other strategies by stopping the load balancer and restarting it:

   ```powershell
   dotnet run --project src/EndJinx.LoadBalancer -- 9000 8000 8001 --strategy random
   dotnet run --project src/EndJinx.LoadBalancer -- 9000 8000 8001 --strategy least-connections
   ```

5. Test health recovery by stopping one backend. After the next five-second
   health-check interval, the load balancer should stop selecting it. Restart
   the backend and it should be selected again after it recovers.

Stop each application with `Ctrl+C`.

## Load testing

Start the HTTP server first, then run one of the scripts from the repository
root.

PowerShell:

```powershell
.\load-test.ps1
.\load-test.ps1 -ConcurrencyLevels @(10, 50, 100) -RequestsPerLevel 500
.\load-test.ps1 -KeepConnectionsOpen
```

Bash:

```bash
./load-test.sh
./load-test.sh -l 10,50,100 -n 500
./load-test.sh --keep-alive -l 25,100,250
```

The scripts report successes, failures, average latency, and throughput.
