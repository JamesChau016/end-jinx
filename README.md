# Nginy

A from-scratch clone of nginx, written in C# on .NET. **Part 1** covers the core HTTP server: raw TCP sockets, manual HTTP/1.1 parsing, static file serving, basic reverse proxying, and an nginx-style config file — no `HttpListener`, no Kestrel, no ASP.NET Core middleware.

## Why build this

Nginx is a great case study for systems programming: socket handling, protocol parsing, concurrency, and config-driven behavior all in one project. Building a slice of it from raw sockets forces you to actually understand HTTP instead of relying on a framework to hide it.

## Scope of Part 1

- **Transport**: raw `System.Net.Sockets`, one async `Task` per connection (no thread-per-connection, no blocking calls).
- **Protocol**: hand-rolled HTTP/1.1 parsing — request line, headers, `Content-Length` bodies. No chunked request bodies yet.
- **Static file serving**: serve files from a document root, MIME type detection by extension, directory index files, 404/403 handling, path traversal protection.
- **Reverse proxy**: forward matched routes to an upstream host:port and relay the response back unmodified.
- **Config file**: custom nginx-style syntax (`server { listen ...; root ...; location { ... } }`), parsed by a hand-written tokenizer/parser — not JSON, not hardcoded.
- **Timeouts**: request read timeout, response write timeout, upstream timeout.
- **Logging**: one line per request (client address, method, path, status code).

Not in scope for Part 1: TLS/HTTPS, HTTP/2, chunked transfer-encoding on the way in, caching, load balancing across multiple upstreams, virtual hosts (multiple `server` blocks).

## Architecture

```
Program.cs
  └─ loads config, starts the server

ConfigParser            → reads the .conf file into a ServerConfiguration
HttpServer              → owns the listening socket, accepts connections
ConnectionHandler        → per-connection loop: read → parse → route → handle → write
RequestParser           → raw bytes → HttpRequest (method, path, version, headers, body)
RouteMatcher            → picks the longest-matching location block for a path
StaticFileHandler        → resolves files under the document root, builds responses
MimeTypeResolver        → file extension → Content-Type
ReverseProxyHandler     → forwards requests to an upstream, relays the response
ResponseWriter          → serializes HttpResponse → wire bytes (status line, headers, body)
Logger                 → per-request log line
```

Each parsing/formatting piece (config parser, request parser, route matcher, MIME resolver, response writer) is written as a pure function so it can be tested in isolation, independent of sockets.

## Example config

```nginx
server {
    listen 8080;
    root ./public;
    index index.html;

    location /api/ {
        proxy_pass 127.0.0.1:5000;
    }

    location / {
        # falls back to static file serving
    }
}
```

## Getting started

```bash
dotnet build
dotnet run -- path/to/nginy.conf
```

The server reads the config path from the first command-line argument, binds to the configured address/port, and starts accepting connections.

## Project layout

```
MyWebServer/
├── Program.cs           # entry point: load config, start server
├── Config/              # config model + parser
├── Http/                # request/response models, parser, response writer
├── Routing/             # route matching
├── Static/              # static file handler + MIME resolver
├── Proxy/               # reverse proxy handler
├── Server/              # listener loop + connection handler
└── Logging/             # request logging
```

(Namespaces above reflect the planned structure — some may not exist yet as you build incrementally.)

## Roadmap

- **Part 1** (this doc): static file serving + reverse proxy over raw HTTP/1.1
- **Part 2** (future): TLS termination, keep-alive tuning, load balancing across multiple upstreams, virtual hosts, gzip
