using System.Net;
using System.Net.Sockets;
using System.Text;
using EndJinx.Connection;

namespace EndJinx.Tests;

public class ConnectionIntegrationTests
{
    [Fact]
    public async Task Server_HandlesTwoConnectionsConcurrently()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var firstServerTask = AcceptAndHandleAsync(listener);
        var secondServerTask = AcceptAndHandleAsync(listener);
        var firstClientTask = ConnectAndRequestAsync((IPEndPoint)listener.LocalEndpoint);
        var secondClientTask = ConnectAndRequestAsync((IPEndPoint)listener.LocalEndpoint);

        var responses = await Task.WhenAll(firstClientTask, secondClientTask);
        await Task.WhenAll(firstServerTask, secondServerTask);

        Assert.All(responses, response =>
        {
            Assert.Contains("HTTP/1.1 200 OK\r\n", response);
            Assert.Contains("Hello, World!", response);
        });
    }

    [Fact]
    public async Task Server_ServesMultipleRequestsOverOneKeepAliveConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        var serverTask = AcceptAndHandleAsync(listener);
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        await using var stream = client.GetStream();

        await WriteRequestAsync(stream, "keep-alive");
        var firstResponse = await ReadResponseAsync(stream);

        await WriteRequestAsync(stream, "close");
        var secondResponse = await ReadResponseAsync(stream);

        await serverTask;

        Assert.Contains("Connection: keep-alive\r\n", firstResponse);
        Assert.Contains("Hello, World!", firstResponse);
        Assert.Contains("Connection: close\r\n", secondResponse);
        Assert.Contains("Hello, World!", secondResponse);
    }

    private static async Task AcceptAndHandleAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await new HttpConnection().handleClient(client);
    }

    private static async Task<string> ConnectAndRequestAsync(IPEndPoint endpoint)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        await using var stream = client.GetStream();

        await WriteRequestAsync(stream, "close");
        return await ReadResponseAsync(stream);
    }

    private static async Task WriteRequestAsync(NetworkStream stream, string connection)
    {
        var request = "GET / HTTP/1.1\r\n" +
                      "Host: localhost\r\n" +
                      $"Connection: {connection}\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(bytes);
    }

    private static async Task<string> ReadResponseAsync(NetworkStream stream)
    {
        using var response = new MemoryStream();
        var buffer = new byte[1024];
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var bytesRead = await stream.ReadAsync(buffer);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Connection closed before response headers were complete.");
            }

            response.Write(buffer, 0, bytesRead);
            headerEnd = FindHeaderEnd(response.GetBuffer(), (int)response.Length);
        }

        var responseText = Encoding.ASCII.GetString(response.GetBuffer(), 0, (int)response.Length);
        var contentLength = GetContentLength(responseText);
        var bodyLength = (int)response.Length - headerEnd - 4;

        while (bodyLength < contentLength)
        {
            var bytesRead = await stream.ReadAsync(buffer);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Connection closed before the response body was complete.");
            }

            response.Write(buffer, 0, bytesRead);
            bodyLength += bytesRead;
        }

        return Encoding.ASCII.GetString(response.GetBuffer(), 0, (int)response.Length);
    }

    private static int GetContentLength(string response)
    {
        const string prefix = "Content-Length:";
        var line = response.Split("\r\n")
            .First(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return int.Parse(line[prefix.Length..].Trim());
    }

    private static int FindHeaderEnd(byte[] bytes, int length)
    {
        for (var index = 0; index <= length - 4; index++)
        {
            if (bytes[index] == '\r' && bytes[index + 1] == '\n'
                && bytes[index + 2] == '\r' && bytes[index + 3] == '\n')
            {
                return index;
            }
        }

        return -1;
    }
}
