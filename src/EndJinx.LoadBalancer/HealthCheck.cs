using System.Net.Sockets;
namespace EndJinx.LoadBalancer;


public class HealthCheck
{
    public async Task<bool> CheckAsync(Backend backendObj)
    {
        using (var backend = new TcpClient())
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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