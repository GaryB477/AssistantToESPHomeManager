using System.Net.Sockets;
using Google.Protobuf;

var host = "192.168.0.26";
var port = 6053;

var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
await socket.ConnectAsync(host, port);

var stream = new NetworkStream(socket);
Console.WriteLine($"Connected to {host}:{port}");

// Send HelloRequest
var helloRequest = new HelloRequest
{
    ClientInfo = "ESP_Home_interactor",
    ApiVersionMajor = 1,
    ApiVersionMinor = 13
};

await SendMessage(stream, 1, helloRequest);
Console.WriteLine("Sent HelloRequest");

// Read HelloResponse
var (msgType, msgData) = await ReadMessage(stream);
Console.WriteLine($"Received message type: {msgType}");

if (msgType == 2) // HelloResponse
{
    var helloResponse = HelloResponse.Parser.ParseFrom(msgData);
    Console.WriteLine($"Server: {helloResponse.ServerInfo}");
    Console.WriteLine($"API: {helloResponse.ApiVersionMajor}.{helloResponse.ApiVersionMinor}");
    Console.WriteLine($"Name: {helloResponse.Name}");
}

Console.ReadLine();
socket.Close();

static async Task SendMessage(NetworkStream stream, uint messageType, IMessage message)
{
    var data = message.ToByteArray();
    var header = new byte[3];
    header[0] = 0x00; // Preamble
    header[1] = (byte)data.Length;
    header[2] = (byte)messageType;

    await stream.WriteAsync(header);
    await stream.WriteAsync(data);
    await stream.FlushAsync();
}

static async Task<(uint type, byte[] data)> ReadMessage(NetworkStream stream)
{
    var header = new byte[3];
    await stream.ReadExactlyAsync(header);

    var length = header[1];
    var type = header[2];

    var data = new byte[length];
    await stream.ReadExactlyAsync(data);

    return (type, data);
}