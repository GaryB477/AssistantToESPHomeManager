using System.Net.Sockets;

var host = "192.168.0.26";
var port = 6053;

var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
await socket.ConnectAsync(host, port);
var isSocketConnected = socket.Connected;

var stream = new NetworkStream(socket);

Console.WriteLine($"Connected to {host}:{port}");

// Keep connection open
Console.ReadLine();

socket.Close();