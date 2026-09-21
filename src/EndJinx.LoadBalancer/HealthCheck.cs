using System.Net.Sockets;
namespace EndJinx.LoadBalancer;


public class HealthCheck
{
    private readonly int _timeoutMilliseconds;

    public HealthCheck(int timeoutMilliseconds = 5000)
    {
        _timeoutMilliseconds = timeoutMilliseconds;
    }

    public async Task<bool> CheckAsync(Backend backendObj)
    {
        using (var backend = new TcpClient())
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_timeoutMilliseconds));
                await backend.ConnectAsync(backendObj.Host, backendObj.Port, cts.Token);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}