# Backend APIs

Small REST backends for manually testing the EndJinx load balancer. Both
instances share a SQLite database, while each response still includes the
backend port so different load-balancer choices are easy to see.

Start two instances from the repository root:

```powershell
dotnet run --project BackendApis/BackendApis.csproj -- 8000
dotnet run --project BackendApis/BackendApis.csproj -- 8001
```

Both commands use `BackendApis/data/items.db`. To use a different shared
database file, pass it as the second argument:

```powershell
dotnet run --project BackendApis/BackendApis.csproj -- 8000 C:\temp\endjinx-items.db
```

Test the REST resources directly:

```powershell
curl.exe -i http://127.0.0.1:8000/api/items
curl.exe -i http://127.0.0.1:8000/api/items/1

$body = @{ name = "third item"; description = "Created with REST" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8000/api/items `
	-ContentType "application/json" -Body $body

$body = @{ name = "updated item"; description = "Updated with REST" } | ConvertTo-Json
Invoke-RestMethod -Method Put -Uri http://127.0.0.1:8000/api/items/1 `
	-ContentType "application/json" -Body $body

Invoke-RestMethod -Method Delete -Uri http://127.0.0.1:8000/api/items/2
Invoke-RestMethod -Uri http://127.0.0.1:8000/health
```

Run the complete CRUD smoke test with PowerShell:

```powershell
.\BackendApis\test-api.ps1
```

To test through the L7 load balancer instead:

```powershell
.\BackendApis\test-api.ps1 -BaseUrl http://127.0.0.1:9000
```

Then start the load balancer using the L7 configuration in
`config/load-balancer.yaml` and test through port 9000:

```powershell
curl.exe -i http://127.0.0.1:9000/api/items
curl.exe -i http://127.0.0.1:9000/api/items/1
```

The load-balancer console and each JSON response show which backend handled
the request. The API supports `GET`, `POST`, `PUT`, and `DELETE` for
`/api/items` resources.

Pass the server root to `test-api.ps1`, not an individual endpoint. For
example, use `http://127.0.0.1:9000`, not `http://127.0.0.1:9000/health`.
