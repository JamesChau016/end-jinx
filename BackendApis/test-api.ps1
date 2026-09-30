param(
    [string]$BaseUrl = "http://127.0.0.1:8000"
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')
$baseUri = [Uri]$BaseUrl
if ($baseUri.AbsolutePath -ne "/") {
    throw "-BaseUrl must be the server root, for example http://127.0.0.1:9000, not an endpoint such as /health."
}

function Invoke-ApiRequest {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body
    )

    $parameters = @{
        Method = $Method
        Uri = "$BaseUrl$Path"
    }

    if ($null -ne $Body) {
        $parameters.ContentType = "application/json"
        $parameters.Body = $Body | ConvertTo-Json -Depth 5
    }

    return Invoke-RestMethod @parameters
}

Write-Host "Testing REST API at $BaseUrl"
Write-Host ""

Write-Host "GET /health"
$health = Invoke-ApiRequest -Method Get -Path "/health"
$health | ConvertTo-Json -Depth 5

Write-Host ""
Write-Host "GET /api/items"
$items = Invoke-ApiRequest -Method Get -Path "/api/items"
$items | ConvertTo-Json -Depth 5

Write-Host ""
Write-Host "POST /api/items"
$created = Invoke-ApiRequest -Method Post -Path "/api/items" -Body @{
    name = "script item"
    description = "Created by test-api.ps1"
}
$created | ConvertTo-Json -Depth 5

Write-Host ""
Write-Host "GET /api/items/$($created.id)"
$found = Invoke-ApiRequest -Method Get -Path "/api/items/$($created.id)"
$found | ConvertTo-Json -Depth 5

Write-Host ""
Write-Host "PUT /api/items/$($created.id)"
$updated = Invoke-ApiRequest -Method Put -Path "/api/items/$($created.id)" -Body @{
    name = "updated script item"
    description = "Updated by test-api.ps1"
}
$updated | ConvertTo-Json -Depth 5

Write-Host ""
Write-Host "DELETE /api/items/$($created.id)"
Invoke-ApiRequest -Method Delete -Path "/api/items/$($created.id)" | Out-Null
Write-Host "Deleted successfully"

Write-Host ""
Write-Host "REST smoke test passed"