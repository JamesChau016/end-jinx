using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EndJinx.Parser;
using EndJinx.Builder;
using EndJinx.Connection;
using EndJinx.Router;
using Microsoft.VisualBasic;


// Simple router to handle different endpoints

var server = new TcpListener(IPAddress.Loopback, 8000);
server.Start();
Console.WriteLine("Connection started");

while (true) {
    var client = await server.AcceptTcpClientAsync();
    var connection = new HttpConnection();
    _ = Task.Run(() => connection.handleClient(client));
}


