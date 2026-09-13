using System.Net.Sockets;

namespace EndJinx.LoadBalancer;

public sealed class TcpProxy
{
    private readonly string _backendHost;
    private readonly int _backendPort;

    public TcpProxy(string backendHost, int backendPort)
    {
        _backendHost = backendHost;
        _backendPort = backendPort;
    }

    public async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var backend = new TcpClient())
        {
            try
            {
                await backend.ConnectAsync(_backendHost, _backendPort);

                using NetworkStream clientStream = client.GetStream();
                using NetworkStream backendStream = backend.GetStream();

                Task clientToBackend = CopyAsync(clientStream, backendStream);
                Task backendToClient = CopyAsync(backendStream, clientStream);

                await Task.WhenAny(clientToBackend, backendToClient);
            }
            catch (SocketException exception)
            {
                Console.Error.WriteLine($"Backend connection failed: {exception.Message}");
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"Proxy connection failed: {exception.Message}");
            }
        }
    }

    private static async Task CopyAsync(NetworkStream source, NetworkStream destination)
    {
        try
        {
            await source.CopyToAsync(destination);
        }
        catch (IOException)
        {
            // The other side may close while the copy is still running.
        }
    }
}
