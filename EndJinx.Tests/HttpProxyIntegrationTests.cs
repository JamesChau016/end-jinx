using System.Net;
using System.Net.Sockets;
using System.Text;
using EndJinx.LoadBalancer;

namespace EndJinx.Tests;

public class HttpProxyIntegrationTests
{
    [Fact]
    public async Task Proxy_RoutesRequestByPathPrefix()
    {
        using var backendListener = new TcpListener(IPAddress.Loopback, 0);
        using var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        backendListener.Start();
        proxyListener.Start();

        var backendEndpoint = (IPEndPoint)backendListener.LocalEndpoint;
        var proxyEndpoint = (IPEndPoint)proxyListener.LocalEndpoint;
        var selector = new PoolSelector([new Backend("127.0.0.1", backendEndpoint.Port)]);
        var proxy = new HttpProxy(
            [new RouteConfiguration { PathPrefix = "/api", Pool = "api" }],
            new Dictionary<string, PoolSelector> { ["api"] = selector });

        var backendTask = AcceptBackendAndRespondAsync(backendListener);
        var proxyTask = AcceptProxyClientAsync(proxyListener, proxy);

        using var client = new TcpClient();
        await client.ConnectAsync(proxyEndpoint.Address, proxyEndpoint.Port);
        await using var clientStream = client.GetStream();
        await clientStream.WriteAsync(Encoding.ASCII.GetBytes(
            "GET /api/items HTTP/1.1\r\nHost: localhost\r\nConnection: keep-alive\r\n\r\n"));

        using var responseBuffer = new MemoryStream();
        await clientStream.CopyToAsync(responseBuffer);

        await Task.WhenAll(backendTask, proxyTask);

        var responseText = Encoding.ASCII.GetString(responseBuffer.ToArray());
        Assert.Contains("200 OK", responseText);
        Assert.Contains("api response", responseText);
    }

    private static async Task AcceptBackendAndRespondAsync(TcpListener listener)
    {
        using var backend = await listener.AcceptTcpClientAsync();
        await using var stream = backend.GetStream();
        var request = await ReadUntilHeadersAsync(stream);
        Assert.StartsWith("GET /api/items", Encoding.ASCII.GetString(request));
        Assert.Contains("Connection: close", Encoding.ASCII.GetString(request));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 11\r\nConnection: close\r\n\r\napi response"));
    }

    private static async Task AcceptProxyClientAsync(TcpListener listener, HttpProxy proxy)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await proxy.HandleAsync(client);
    }

    private static async Task<byte[]> ReadUntilHeadersAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        byte[] headerMarker = [13, 10, 13, 10];
        var buffer = new byte[128];
        while (!bytes.Skip(Math.Max(0, bytes.Count - 4)).SequenceEqual(headerMarker))
        {
            var count = await stream.ReadAsync(buffer);
            bytes.AddRange(buffer.AsSpan(0, count).ToArray());
        }

        return bytes.ToArray();
    }
}