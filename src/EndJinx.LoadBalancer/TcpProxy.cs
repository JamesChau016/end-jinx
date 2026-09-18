using System.Net.Sockets;

namespace EndJinx.LoadBalancer;

public sealed class TcpProxy
{
    private readonly string _backendHost;
    private readonly int _backendPort;

    public TcpProxy(Backend backendObj)
    {
        _backendHost = backendObj.Host;
        _backendPort = backendObj.Port;
    }

    public async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var backend = new TcpClient())
        using (var shutdown = new CancellationTokenSource())
        {
            try
            {
                await backend.ConnectAsync(_backendHost, _backendPort);

                using NetworkStream clientStream = client.GetStream();
                using NetworkStream backendStream = backend.GetStream();

                Task<bool> clientToBackend = CopyAsync(clientStream, backendStream, shutdown.Token);
                Task<bool> backendToClient = CopyAsync(backendStream, clientStream, shutdown.Token);

                Task completed = await Task.WhenAny(clientToBackend, backendToClient);

                if (completed == clientToBackend)
                {
                    if (await clientToBackend)
                    {
                        HalfCloseSend(backend);
                    }
                    else
                    {
                        shutdown.Cancel();
                    }

                    await backendToClient;
                }
                else
                {
                    HalfCloseSend(client);
                    shutdown.Cancel();
                    await clientToBackend;
                }

                shutdown.Cancel();
            }
            catch (SocketException exception)
            {
                Console.Error.WriteLine($"Backend connection failed: {exception.Message}");
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"Proxy connection failed: {exception.Message}");
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal shutdown path when either side closes.
            }
        }
    }

    private static void HalfCloseSend(TcpClient client)
    {
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
            // The peer may already have closed the connection.
        }
        catch (ObjectDisposedException)
        {
            // The connection is already shutting down.
        }
    }

    private static async Task<bool> CopyAsync(
        NetworkStream source,
        NetworkStream destination,
        CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, cancellationToken);
            return true;
        }
        catch (IOException)
        {
            // The other side may close while the copy is still running.
            return false;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal shutdown path when the other copy ends.
            return false;
        }
    }
}
