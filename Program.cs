using System.Net;
using System.Net.Sockets;
using EndJinx.Connection;
using EndJinx.Logging;


internal static class Program
{
    private static async Task Main()
    {
        // Simple router to handle different endpoints
        var server = new TcpListener(IPAddress.Any, 8000);
        var logger = new ConsoleLogger();
        server.Start();
        logger.Info("Connection started");

        while (true)
        {
            var client = await server.AcceptTcpClientAsync();
            var connection = new HttpConnection(logger);
            _ = Task.Run(() => connection.handleClient(client));
        }
    }
}


