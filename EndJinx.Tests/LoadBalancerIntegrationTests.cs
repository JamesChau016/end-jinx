using System.Net;
using System.Net.Sockets;
using System.Text;
using EndJinx.LoadBalancer;

namespace EndJinx.Tests;

public class LoadBalancerIntegrationTests
{
    [Fact]
    public async Task Proxy_ForwardsResponseAndClosesWhenBackendCloses()
    {
        using var backendListener = new TcpListener(IPAddress.Loopback, 0);
        using var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        backendListener.Start();
        proxyListener.Start();

        var backendEndpoint = (IPEndPoint)backendListener.LocalEndpoint;
        var proxyEndpoint = (IPEndPoint)proxyListener.LocalEndpoint;
        var proxy = new TcpProxy(new Backend("127.0.0.1", backendEndpoint.Port));

        var backendTask = AcceptBackendAndRespondAsync(backendListener);
        var proxyTask = AcceptProxyClientAsync(proxyListener, proxy);

        using var client = new TcpClient();
        await client.ConnectAsync(proxyEndpoint.Address, proxyEndpoint.Port);
        await using var clientStream = client.GetStream();

        await clientStream.WriteAsync(Encoding.ASCII.GetBytes("request"));
        client.Client.Shutdown(SocketShutdown.Send);

        var response = new byte[8];
        var bytesRead = await clientStream.ReadAsync(response);
        var endOfStream = await clientStream.ReadAsync(response);

        await Task.WhenAll(backendTask, proxyTask);

        Assert.Equal("response", Encoding.ASCII.GetString(response, 0, bytesRead));
        Assert.Equal(0, endOfStream);
    }

    private static async Task AcceptBackendAndRespondAsync(TcpListener listener)
    {
        using var backend = await listener.AcceptTcpClientAsync();
        await using var stream = backend.GetStream();
        var request = new byte[7];
        await stream.ReadExactlyAsync(request);
        Assert.Equal("request", Encoding.ASCII.GetString(request));
        await stream.WriteAsync(Encoding.ASCII.GetBytes("response"));
    }

    private static async Task AcceptProxyClientAsync(TcpListener listener, TcpProxy proxy)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await proxy.HandleAsync(client);
    }
}
