using System.Net.Sockets;
using System.Text;

namespace EndJinx.LoadBalancer;

public sealed class HttpProxy
{
    private const int MaxHeaderSize = 8 * 1024;
    private readonly IReadOnlyList<RouteConfiguration> _routes;
    private readonly IReadOnlyDictionary<string, PoolSelector> _selectors;
    private readonly Action<Backend>? _backendFailureHandler;
    private readonly Action<Backend>? _backendCompletionHandler;

    public HttpProxy(
        IReadOnlyList<RouteConfiguration> routes,
        IReadOnlyDictionary<string, PoolSelector> selectors,
        Action<Backend>? backendFailureHandler = null,
        Action<Backend>? backendCompletionHandler = null)
    {
        _routes = routes;
        _selectors = selectors;
        _backendFailureHandler = backendFailureHandler;
        _backendCompletionHandler = backendCompletionHandler;
    }

    public async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            Backend? backend = null;
            try
            {
                using NetworkStream clientStream = client.GetStream();
                var request = await ReadRequestAsync(clientStream);
                if (request.Length == 0)
                {
                    return;
                }

                var path = ParsePath(request);
                var route = _routes.FirstOrDefault(candidate => path.StartsWith(
                    candidate.PathPrefix, StringComparison.OrdinalIgnoreCase));
                if (route is null || !_selectors.TryGetValue(route.Pool, out var selector))
                {
                    await WriteErrorAsync(clientStream, 404, "Not Found", "No route is configured for this path.");
                    return;
                }

                backend = selector.Next();
                Console.WriteLine($"Routing {path} to {backend.Host}:{backend.Port} (pool '{route.Pool}')");
                using var upstream = new TcpClient();
                await upstream.ConnectAsync(backend.Host, backend.Port);
                await using NetworkStream upstreamStream = upstream.GetStream();

                await upstreamStream.WriteAsync(ForceConnectionClose(request));
                await upstreamStream.CopyToAsync(clientStream);
            }
            catch (InvalidOperationException)
            {
                if (client.Connected)
                {
                    await WriteErrorAsync(client.GetStream(), 503, "Service Unavailable", "No healthy backend is available.");
                }
            }
            catch (SocketException exception)
            {
                Console.Error.WriteLine($"HTTP backend connection failed: {exception.Message}");
                if (backend is not null)
                {
                    _backendFailureHandler?.Invoke(backend);
                }

                if (client.Connected)
                {
                    await WriteErrorAsync(client.GetStream(), 502, "Bad Gateway", "The selected backend could not be reached.");
                }
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"HTTP proxy connection failed: {exception.Message}");
            }
            finally
            {
                if (backend is not null)
                {
                    _backendCompletionHandler?.Invoke(backend);
                }
            }
        }
    }

    private static async Task<byte[]> ReadRequestAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1024];
        var marker = new byte[] { 13, 10, 13, 10 };
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return Array.Empty<byte>();
            }

            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            headerEnd = IndexOf(bytes, marker);
            if (bytes.Count > MaxHeaderSize)
            {
                throw new IOException("HTTP request headers are too large.");
            }
        }

        var headerText = Encoding.ASCII.GetString(bytes.GetRange(0, headerEnd).ToArray());
        var contentLength = ParseContentLength(headerText);
        var requestLength = headerEnd + marker.Length + contentLength;
        while (bytes.Count < requestLength)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                throw new IOException("HTTP request body ended early.");
            }

            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return bytes.Take(requestLength).ToArray();
    }

    private static string ParsePath(byte[] request)
    {
        var requestLine = Encoding.ASCII.GetString(request).Split("\r\n", 2)[0];
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[1].StartsWith('/'))
        {
            throw new IOException("Malformed HTTP request line.");
        }

        return parts[1].Split('?', 2)[0];
    }

    private static byte[] ForceConnectionClose(byte[] request)
    {
        var marker = new byte[] { 13, 10, 13, 10 };
        var separator = IndexOf(request, marker);
        var headers = Encoding.ASCII.GetString(request, 0, separator)
            .Split("\r\n")
            .Where(line => !line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase));
        var newHeaders = Encoding.ASCII.GetBytes(
            string.Join("\r\n", headers) + "\r\nConnection: close\r\n\r\n");
        return newHeaders.Concat(request[(separator + marker.Length)..]).ToArray();
    }

    private static int ParseContentLength(string headers)
    {
        var line = headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        return line is not null && int.TryParse(line[15..].Trim(), out var length) && length >= 0 ? length : 0;
    }

    private static int IndexOf(List<byte> bytes, byte[] marker)
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

    private static int IndexOf(byte[] bytes, byte[] marker)
    {
        for (var index = 0; index <= bytes.Length - marker.Length; index++)
        {
            if (bytes.AsSpan(index, marker.Length).SequenceEqual(marker))
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task WriteErrorAsync(NetworkStream stream, int status, string reason, string message)
    {
        var body = Encoding.UTF8.GetBytes(message);
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
        await stream.WriteAsync(body);
    }
}