# EndJinx

EndJinx is a small HTTP/1.1 web server written in C# over raw TCP. It is a
learning project for understanding HTTP framing, parsing, connection
lifecycle, concurrency, and response serialization without using Kestrel,
ASP.NET Core, or `HttpListener`.

## Requirements

- .NET SDK 10.0 or later
- `curl` for the command-line examples and shell load test
- PowerShell 5.1 or later for `load-test.ps1`

## Current functionality

- Listens on `0.0.0.0:8000`.
- Accepts multiple TCP clients concurrently.
- Reads request headers until `\r\n\r\n`, even when headers arrive across
  multiple network reads.
- Reads request bodies using `Content-Length`.
- Supports HTTP/1.1 keep-alive and multiple requests on one connection.
- Closes idle or incomplete reads after five seconds.
- Limits headers to 8 KiB and request bodies to 1 MiB.
- Returns well-formed error responses for malformed requests, oversized
  bodies, missing routes, and unexpected server errors.

TLS is not implemented, so the server currently speaks plain HTTP only.

## Routes

| Method | Path            | Response                       |
| ------ | --------------- | ------------------------------ |
| `GET`  | `/`             | `200 OK` with `Hello, World!`  |
| `POST` | `/echo`         | `200 OK` with the request body |
| Any    | Any other route | `404 Not Found`                |

The server returns `400 Bad Request` for malformed HTTP or invalid body
lengths, `413 Content Too Large` for bodies over 1 MiB, and
`500 Internal Server Error` for unexpected failures.

## Project structure

```text
EndJinx.csproj                 Main executable project
Program.cs                     TCP listener and concurrent accept loop
HttpConnection.cs              Per-client request/response loop
RequestParser.cs               HTTP framing and request parsing
ResponseBuilder.cs             HTTP response serialization
Router.cs                      Method/path routing and response creation
Exceptions.cs                  HTTP exception types and status codes
Logger.cs                      Console logging abstraction
EndJinx.Tests/                 Unit and integration tests
  ServerTests.cs               Parser, router, and response tests
  ConnectionIntegrationTests.cs Keep-alive and concurrent connection tests
requests.http                 Sample requests for an HTTP client extension
load-test.ps1                  Concurrent load test for PowerShell
load-test.sh                   Concurrent load test for Bash and curl
src/LoadBalancer/              Future load-balancer project scaffold
```

The load balancer is not implemented yet. Its learning plan is documented in
`LOAD_BALANCER_LEARNING_PLAN.md`.

## Run the server

From the repository root:

```bash
dotnet run --project EndJinx.csproj
```

The server logs to the console and listens at `http://127.0.0.1:8000`.
Stop it with `Ctrl+C`.

## Try it with curl

With the server running in another terminal:

```bash
curl -i http://127.0.0.1:8000/

curl -i -X POST http://127.0.0.1:8000/echo \
  -H "Content-Type: text/plain" \
  --data "hello from EndJinx"

curl -i http://127.0.0.1:8000/missing
```

The same examples, plus malformed-request examples, are in `requests.http`
for use with an HTTP client extension in VS Code.

## Build and test

Build the server:

```bash
dotnet build EndJinx.csproj
```

Run the unit and integration test suite:

```bash
dotnet test EndJinx.Tests/EndJinx.Tests.csproj
```

The tests cover request parsing, body-length validation, routing, response
headers, concurrent connections, and multiple requests over one keep-alive
connection.

## Load testing

Start EndJinx first, then run one of the scripts from the repository root.

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

Both scripts report successes, failures, average latency, and throughput at
several concurrency levels. The Bash script requires `curl`.

## Design notes

The parser, router, response builder, and logger are separate from the TCP
accept loop so most behavior can be tested without real sockets. Each client
is handled in its own task, while the connection handler owns the request
loop, keep-alive decision, and per-connection cleanup.

This project intentionally keeps the protocol surface small. Static files,
TLS, chunked transfer encoding, richer HTTP methods, and load balancing are
future work rather than supported features today.
