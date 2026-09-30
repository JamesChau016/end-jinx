using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

var port = args.Length == 0 ? 8000 : ParsePort(args[0]);
var databasePath = args.Length > 1 ? args[1] : GetDefaultDatabasePath();
var listener = new TcpListener(IPAddress.Any, port);
var store = new RestStore(port, databasePath);
listener.Start();

Console.WriteLine($"REST backend listening on http://127.0.0.1:{port}");
Console.WriteLine($"Shared database: {Path.GetFullPath(databasePath)}");
Console.WriteLine("Routes: GET/POST /api/items, GET/PUT/DELETE /api/items/{id}, GET /health");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClientAsync(client, store));
}

static async Task HandleClientAsync(TcpClient client, RestStore store)
{
    using (client)
    await using (var stream = client.GetStream())
    {
        try
        {
            var request = await ReadRequestAsync(stream);
            if (request is null)
            {
                return;
            }

            await WriteResponseAsync(stream, HandleRequest(request, store));
        }
        catch (IOException)
        {
        }
    }
}

static BackendResponse HandleRequest(RestRequest request, RestStore store)
{
    var path = request.Path.Split('?', 2)[0];

    if (request.Method == "GET" && path == "/health")
    {
        return JsonResponse(200, new { status = "healthy", backend = store.Backend });
    }

    if (request.Method == "GET" && path == "/static/status")
    {
        return JsonResponse(200, new
        {
            service = "static-backend",
            backend = store.Backend,
            message = "Static response"
        });
    }

    if (!path.StartsWith("/api/items", StringComparison.OrdinalIgnoreCase))
    {
        return JsonResponse(404, new { error = "Not Found", path });
    }

    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length == 2)
    {
        return HandleCollectionRequest(request, store);
    }

    if (segments.Length == 3 && int.TryParse(segments[2], out var id))
    {
        return HandleItemRequest(request, store, id);
    }

    return JsonResponse(404, new { error = "Not Found", path });
}

static BackendResponse HandleCollectionRequest(RestRequest request, RestStore store)
{
    if (request.Method == "GET")
    {
        return JsonResponse(200, store.GetAll());
    }

    if (request.Method == "POST")
    {
        var input = ParseBody(request);
        if (input is null || string.IsNullOrWhiteSpace(input.Name))
        {
            return JsonResponse(400, new { error = "Body must contain a non-empty name." });
        }

        var item = store.Create(input);
        return JsonResponse(201, item, new Dictionary<string, string>
        {
            ["Location"] = $"/api/items/{item.Id}"
        });
    }

    return JsonResponse(405, new { error = "Method Not Allowed" });
}

static BackendResponse HandleItemRequest(RestRequest request, RestStore store, int id)
{
    return request.Method switch
    {
        "GET" => store.Get(id) is { } item
            ? JsonResponse(200, item)
            : JsonResponse(404, new { error = "Item not found", id }),
        "PUT" => UpdateItem(request, store, id),
        "DELETE" => store.Delete(id)
            ? new BackendResponse(204, "", Array.Empty<byte>(), new Dictionary<string, string>())
            : JsonResponse(404, new { error = "Item not found", id }),
        _ => JsonResponse(405, new { error = "Method Not Allowed" })
    };
}

static BackendResponse UpdateItem(RestRequest request, RestStore store, int id)
{
    var input = ParseBody(request);
    if (input is null || string.IsNullOrWhiteSpace(input.Name))
    {
        return JsonResponse(400, new { error = "Body must contain a non-empty name." });
    }

    var item = store.Update(id, input);
    return item is null
        ? JsonResponse(404, new { error = "Item not found", id })
        : JsonResponse(200, item);
}

static RestItemInput? ParseBody(RestRequest request)
{
    try
    {
        using var document = JsonDocument.Parse(request.Body);
        var name = GetJsonString(document.RootElement, "name");
        var description = GetJsonString(document.RootElement, "description");
        return new RestItemInput { Name = name, Description = description };
    }
    catch (JsonException)
    {
        return null;
    }
}

static string? GetJsonString(JsonElement element, string propertyName)
{
    var property = element.EnumerateObject()
        .FirstOrDefault(candidate => candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
}

static BackendResponse JsonResponse(int status, object body, Dictionary<string, string>? headers = null)
{
    return new BackendResponse(
        status,
        "application/json",
        JsonSerializer.SerializeToUtf8Bytes(body, JsonConfiguration.Options),
        headers ?? new Dictionary<string, string>());
}

static async Task WriteResponseAsync(NetworkStream stream, BackendResponse response)
{
    var headers = new StringBuilder()
        .Append($"HTTP/1.1 {response.Status} {GetReason(response.Status)}\r\n")
        .Append($"Content-Type: {response.ContentType}\r\n")
        .Append($"Content-Length: {response.Body.Length}\r\n")
        .Append("Connection: close\r\n");

    foreach (var header in response.Headers)
    {
        headers.Append($"{header.Key}: {header.Value}\r\n");
    }

    headers.Append("\r\n");
    await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()));
    await stream.WriteAsync(response.Body);
}

static async Task<RestRequest?> ReadRequestAsync(NetworkStream stream)
{
    var bytes = new List<byte>();
    var buffer = new byte[1024];
    var marker = new byte[] { 13, 10, 13, 10 };
    var headerEnd = -1;

    while (headerEnd < 0)
    {
        var count = await stream.ReadAsync(buffer);
        if (count == 0)
        {
            return null;
        }

        bytes.AddRange(buffer.AsSpan(0, count).ToArray());
        headerEnd = IndexOf(bytes, marker);
        if (bytes.Count > 8 * 1024)
        {
            return null;
        }
    }

    var headerText = Encoding.ASCII.GetString(bytes.Take(headerEnd).ToArray());
    var headerLines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    var requestLine = headerLines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (requestLine.Length < 3)
    {
        return null;
    }

    var contentLengthHeader = headerLines
        .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    var bodyLength = contentLengthHeader is not null &&
        int.TryParse(contentLengthHeader[15..].Trim(), out var parsedLength)
        ? parsedLength
        : 0;
    var requestLength = headerEnd + marker.Length + bodyLength;

    while (bytes.Count < requestLength)
    {
        var count = await stream.ReadAsync(buffer);
        if (count == 0)
        {
            return null;
        }

        bytes.AddRange(buffer.AsSpan(0, count).ToArray());
    }

    return new RestRequest(
        requestLine[0],
        requestLine[1],
        bytes.Skip(headerEnd + marker.Length).Take(bodyLength).ToArray());
}

static int IndexOf(List<byte> bytes, byte[] marker)
{
    for (var index = 0; index <= bytes.Count - marker.Length; index++)
    {
        if (bytes.Skip(index).Take(marker.Length).SequenceEqual(marker))
        {
            return index;
        }
    }

    return -1;
}

static int ParsePort(string value)
{
    if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
    {
        throw new ArgumentException("The backend port must be between 1 and 65535.");
    }

    return port;
}

static string GetDefaultDatabasePath()
{
    var runningFromRepositoryRoot = Directory.Exists(Path.Combine(Environment.CurrentDirectory, "BackendApis"));
    return runningFromRepositoryRoot
        ? Path.Combine(Environment.CurrentDirectory, "BackendApis", "data", "items.db")
        : Path.Combine(Environment.CurrentDirectory, "data", "items.db");
}

static string GetReason(int status) => status switch
{
    200 => "OK",
    201 => "Created",
    204 => "No Content",
    400 => "Bad Request",
    404 => "Not Found",
    405 => "Method Not Allowed",
    _ => "Error"
};

internal sealed record RestRequest(string Method, string Path, byte[] Body);
internal sealed record RestItem(int Id, string Name, string Description, string Backend);
internal sealed class RestItemInput
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}
internal sealed record BackendResponse(int Status, string ContentType, byte[] Body, Dictionary<string, string> Headers);

internal static class JsonConfiguration
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal sealed class RestStore
{
    private readonly string _connectionString;

    public RestStore(int port, string databasePath)
    {
        Backend = $"127.0.0.1:{port}";
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString();
        InitializeDatabase();
    }

    public string Backend { get; }

    public RestItem[] GetAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, description, backend FROM items ORDER BY id";
        using var reader = command.ExecuteReader();
        var items = new List<RestItem>();
        while (reader.Read())
        {
            items.Add(ReadItem(reader));
        }

        return items.ToArray();
    }

    public RestItem? Get(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, description, backend FROM items WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    public RestItem Create(RestItemInput input)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO items (name, description, backend)
            VALUES ($name, $description, $backend);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", input.Name!);
        command.Parameters.AddWithValue("$description", input.Description ?? "");
        command.Parameters.AddWithValue("$backend", Backend);
        var id = Convert.ToInt32(command.ExecuteScalar());
        return Get(id)!;
    }

    public RestItem? Update(int id, RestItemInput input)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE items
            SET name = $name, description = $description, backend = $backend
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", input.Name!);
        command.Parameters.AddWithValue("$description", input.Description ?? "");
        command.Parameters.AddWithValue("$backend", Backend);
        return command.ExecuteNonQuery() == 0 ? null : Get(id);
    }

    public bool Delete(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM items WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void InitializeDatabase()
    {
        using var connection = OpenConnection();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }

        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL,
                    backend TEXT NOT NULL
                );
                """;
            schema.ExecuteNonQuery();
        }

        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO items (name, description, backend)
            SELECT $firstName, $description, $backend
            WHERE NOT EXISTS (SELECT 1 FROM items);

            INSERT INTO items (name, description, backend)
            SELECT $secondName, $description, $backend
            WHERE (SELECT COUNT(*) FROM items) = 1;
            """;
        seed.Parameters.AddWithValue("$firstName", "first item");
        seed.Parameters.AddWithValue("$secondName", "second item");
        seed.Parameters.AddWithValue("$description", "Seed data");
        seed.Parameters.AddWithValue("$backend", Backend);
        seed.ExecuteNonQuery();
    }

    private static RestItem ReadItem(SqliteDataReader reader)
    {
        return new RestItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }
}