using System.Net;
using System.Net.Sockets;
using EndJinx.Connection;


// Simple router to handle different endpoints

var server = new TcpListener(IPAddress.Loopback, 8000);
server.Start();
Console.WriteLine("Connection started");

while (true) {
    var client = await server.AcceptTcpClientAsync();
    var connection = new HttpConnection();
    _ = Task.Run(() => connection.handleClient(client));
}


