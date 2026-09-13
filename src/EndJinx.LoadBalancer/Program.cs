using System.Net;
using System.Net.Sockets;
using EndJinx.LoadBalancer;

const int listenPort = 9000;
const string backendHost = "127.0.0.1";
const int backendPort = 8000;

var listener = new TcpListener(IPAddress.Any, listenPort);
var proxy = new TcpProxy(backendHost, backendPort);

listener.Start();
Console.WriteLine($"Load balancer listening on port {listenPort}");
Console.WriteLine($"Forwarding TCP connections to {backendHost}:{backendPort}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => proxy.HandleAsync(client));
}
