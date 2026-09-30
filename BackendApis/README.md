# Backend APIs

This folder contains a small REST API used to test the EndJinx L7 load
balancer. Two backend processes can run on different ports while sharing one
SQLite database.

Each response includes the backend port that handled the request. The item
data itself is shared, so a record created through one backend can be read or
updated through the other.

## Start the Backends

Run these commands from the repository root in two terminals:

```powershell
dotnet run --project BackendApis/BackendApis.csproj -- 8000
dotnet run --project BackendApis/BackendApis.csproj -- 8001
```

Both processes use this shared database by default:

```text
BackendApis/data/items.db
```

The port must come after `--`. This is correct:

```powershell
dotnet run --project BackendApis/BackendApis.csproj -- 8001
```

This is not a valid way to pass the port:

```powershell
dotnet run --8001
```

To use a different database file, pass it as a second argument to both
processes:

```powershell
dotnet run --project BackendApis/BackendApis.csproj -- 8000 C:\temp\endjinx-items.db
dotnet run --project BackendApis/BackendApis.csproj -- 8001 C:\temp\endjinx-items.db
```

## Test A Backend Directly

Use `Invoke-RestMethod` for JSON requests in PowerShell:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
Invoke-RestMethod http://127.0.0.1:8000/api/items
Invoke-RestMethod http://127.0.0.1:8000/api/items/1

$body = @{ name = "third item"; description = "Created with REST" } |
    ConvertTo-Json
Invoke-RestMethod -Method Post `
    -Uri http://127.0.0.1:8000/api/items `
    -ContentType "application/json" `
    -Body $body

$body = @{ name = "updated item"; description = "Updated with REST" } |
    ConvertTo-Json
Invoke-RestMethod -Method Put `
    -Uri http://127.0.0.1:8000/api/items/1 `
    -ContentType "application/json" `
    -Body $body

Invoke-RestMethod -Method Delete http://127.0.0.1:8000/api/items/2
```

Available REST routes:

| Method   | Route             | Purpose                   |
| -------- | ----------------- | ------------------------- |
| `GET`    | `/health`         | Backend health and port   |
| `GET`    | `/api/items`      | List items                |
| `POST`   | `/api/items`      | Create an item            |
| `GET`    | `/api/items/{id}` | Get one item              |
| `PUT`    | `/api/items/{id}` | Replace one item          |
| `DELETE` | `/api/items/{id}` | Delete one item           |
| `GET`    | `/static/status`  | Static-pool test response |

## Start The L7 Load Balancer

The current [load-balancer.yaml](../config/load-balancer.yaml) uses:

- `/api/*` and `/health` -> the `api` pool containing ports `8000` and `8001`
- `/static/*` -> the `static` pool containing port `8001`
- `round-robin` selection inside each pool

Start it from a third terminal:

```powershell
dotnet run --project src/EndJinx.LoadBalancer
```

Then run the complete CRUD smoke test through the load balancer:

```powershell
.\BackendApis\test-api.ps1 -BaseUrl http://127.0.0.1:9000
```

The script tests health, list, create, get, update, and delete. Pass the
server root as `-BaseUrl`; do not pass an endpoint such as `/health`.

## Verify Round Robin

Run several independent API requests and print the backend port from each
response:

```powershell
1..6 | ForEach-Object {
    (Invoke-RestMethod http://127.0.0.1:9000/api/items).backend
}
```

The output should alternate between `127.0.0.1:8000` and
`127.0.0.1:8001`. The item data remains available regardless of which backend
answers because both processes use the same SQLite file.

The load balancer must be restarted after changing the YAML configuration.

## Reset The Database

Stop both backend processes first, then remove the database and SQLite WAL
sidecar files:

```powershell
Remove-Item BackendApis/data/items.db* -Force -ErrorAction SilentlyContinue
```

The next backend process to start recreates the database and inserts the two
seed items.

## Troubleshooting

Check whether a port is already occupied:

```powershell
Get-NetTCPConnection -LocalPort 8000,8001,9000 -ErrorAction SilentlyContinue
```

If a port is occupied, stop the old process or choose another backend port and
update [load-balancer.yaml](../config/load-balancer.yaml). All backend
processes must point to the same database path for shared CRUD state.
