using System.Net.Sockets;

namespace EndJinx.LoadBalancer;

public sealed class TcpProxy
{
    private readonly string _backendHost;
    private readonly int _backendPort;
    private readonly Action<Backend>? _backendFailureHandler;
    private readonly Action<Backend>? _backendCompletionHandler;
    private readonly int _timeoutMilliseconds;

    public TcpProxy(
        Backend backendObj,
        Action<Backend>? backendFailureHandler = null,
        Action<Backend>? backendCompletionHandler = null,
        int timeoutMilliseconds = 5000)
    {
        _backendHost = backendObj.Host;
        _backendPort = backendObj.Port;
        _backendFailureHandler = backendFailureHandler;
        _backendCompletionHandler = backendCompletionHandler;
        _timeoutMilliseconds = timeoutMilliseconds > 0 ? timeoutMilliseconds : 5000;
    }

    public async Task<bool> HandleAsync(TcpClient client)
    {
        using (client)
        using (var backend = new TcpClient())
        using (var shutdown = new CancellationTokenSource())
        {
            var backendObject = new Backend(_backendHost, _backendPort);
            try
            {
                using var connectCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_timeoutMilliseconds));
                await backend.ConnectAsync(_backendHost, _backendPort, connectCts.Token);

                using NetworkStream clientStream = client.GetStream();
                using NetworkStream backendStream = backend.GetStream();

                Task<bool> clientToBackend = CopyAsync(clientStream, backendStream, shutdown.Token, _timeoutMilliseconds);
                Task<bool> backendToClient = CopyAsync(backendStream, clientStream, shutdown.Token, _timeoutMilliseconds);

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
                return true;
            }
            catch (SocketException exception)
            {
                Console.Error.WriteLine($"Backend connection failed: {exception.Message}");
                _backendFailureHandler?.Invoke(backendObject);
                return false;
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"Proxy connection failed: {exception.Message}");
                return false;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"Backend request timed out after {_timeoutMilliseconds}ms.");
                _backendFailureHandler?.Invoke(backendObject);
                return false;
            }
            finally
            {
                _backendCompletionHandler?.Invoke(backendObject);
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
        CancellationToken cancellationToken,
        int timeoutMilliseconds)
    {
        try
        {
            var buffer = new byte[8192];
            while (true)
            {
                var read = await ReadWithTimeoutAsync(source, buffer, timeoutMilliseconds, cancellationToken);
                if (read == 0)
                {
                    return true;
                }

                await WriteWithTimeoutAsync(destination, buffer.AsMemory(0, read), timeoutMilliseconds, cancellationToken);
            }
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

    private static async Task<int> ReadWithTimeoutAsync(
        NetworkStream stream,
        byte[] buffer,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        return await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token);
    }

    private static async Task WriteWithTimeoutAsync(
        NetworkStream stream,
        ReadOnlyMemory<byte> data,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        await stream.WriteAsync(data, timeoutCts.Token);
    }
}
