# Repository Guidelines

## Project Structure & Module Organization

The root `EndJinx.csproj` contains the raw TCP HTTP server. Its main responsibilities are split among `HttpConnection.cs`, `RequestParser.cs`, `ResponseBuilder.cs`, `Router.cs`, and `Logger.cs`. The load balancer is a separate project in `src/EndJinx.LoadBalancer/`; configuration models and examples live in `config/`. Unit and integration tests are in `EndJinx.Tests/`. `BackendApis/` contains supporting backend examples, while `benchmark.ps1` and the `load-test.*` scripts support local performance runs. Keep generated `bin/` and `obj/` output out of commits.

## Build, Test, and Development Commands

Run commands from the repository root. Build either application with `dotnet build EndJinx.csproj` or `dotnet build src/EndJinx.LoadBalancer/EndJinx.LoadBalancer.csproj`. Run the full test suite with `dotnet test EndJinx.Tests/EndJinx.Tests.csproj`; narrow it with `--filter FullyQualifiedName~PoolSelectorTests`. Start the HTTP server using `dotnet run --project EndJinx.csproj`; start the load balancer using `dotnet run --project src/EndJinx.LoadBalancer`. The README documents manual curl checks and load-test options.

## Coding Style & Naming Conventions

Use the existing C# style: four-space indentation, nullable reference types, PascalCase for types and public members, and camelCase for locals and parameters. Keep networking, parsing, routing, and selection logic in their focused modules. Name test files and classes after the behavior or component under test, such as `PoolSelectorTests.cs`. Follow nearby code patterns; no repository-wide formatter or linter configuration is present.

## Testing Guidelines

Tests use xUnit and cover parsing, routing, connection handling, proxying, configuration, metrics, and backend selection. Add or update focused tests alongside behavior changes, including integration coverage when changing network lifecycle or proxy behavior. Run the full `dotnet test` command before submitting changes.

## Commit & Pull Request Guidelines

Recent commit subjects are short, descriptive phrases (for example, `Minor refactor`); no enforced conventional-commit format is evident. Use a concise imperative subject that identifies the change. Pull requests should explain the behavior and motivation, list validation commands and results, link related issues when available, and include sample requests or output for user-visible protocol changes.

## Configuration & Safety

Keep sample listener, pool, route, timeout, and retry settings in `config/`. Do not commit credentials or machine-specific settings. When changing protocol limits, health checks, retries, or routing behavior, update the relevant tests and README guidance.
