using System.Net;
using System.Net.Sockets;
using System.Text;


var server = new TcpListener(IPAddress.Any, 8000);
server.Start();
Console.WriteLine("Connection started");

while (true) {
    var client = await server.AcceptTcpClientAsync();
    var stream = client.GetStream();

    var buffer = new byte[1024];
    await stream.ReadAsync(buffer);

    var response = "HTTP/1.1 200 OK\r\nContent-Length: 11\r\n\r\nHello World";
    var bytes = Encoding.UTF8.GetBytes(response);
    await stream.WriteAsync(bytes);
    client.Close();
}