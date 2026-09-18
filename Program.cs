using System.Net;
using System.Net.Sockets;
using EndJinx.Connection;
using EndJinx.Logging;


internal static class Program
{
    private static async Task Main(string[] args)
    {
        int port = args.Length == 0 ? 8000 : ParsePort(args[0], "server");
        var server = new TcpListener(IPAddress.Any, port);
        var logger = new ConsoleLogger();
        server.Start();
        logger.Info($"Connection started on port {port}");

        while (true)
        {
            var client = await server.AcceptTcpClientAsync();
            var connection = new HttpConnection(logger);
            _ = Task.Run(() => connection.handleClient(client));
        }
    }

    private static int ParsePort(string value, string name)
    {
        if (!int.TryParse(value, out int port) || port is < 1 or > 65535)
        {
            throw new ArgumentException($"The {name} port must be between 1 and 65535.", nameof(value));
        }

        return port;
    }
}


