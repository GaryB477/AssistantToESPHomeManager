using System.Net.Sockets;
using Google.Protobuf;

namespace ESP_Home_Interactor;

public class ESP
{
    public ESP(string host = "192.168.0.26", int port = 6053)
    {
        if (string.IsNullOrEmpty(host)) throw new ArgumentNullException(nameof(host));
        Host = host;
        Port = port;
    }
    
    public int Port { get; set; }
    public string Host { get; set; }
    private Socket? _socket;
    private NetworkStream? _stream;
    
    public async Task Init()
    {
        
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await _socket.ConnectAsync(Host, Port);
        _stream = new NetworkStream(_socket);
    }
    
    public async Task Run()
    {
        if (_socket == null || _stream == null) throw new InvalidOperationException("Socket or stream not initialized") ;
        await SendHelloWorld(_stream);
        await Cleanup(_socket, _stream);
    }
    
    private static async Task Cleanup(Socket socket, NetworkStream stream)
    {
        // Send DisconnectRequest
        var disconnectRequest = new DisconnectRequest();
        await SendMessage(stream, 5, disconnectRequest);
        Console.WriteLine("Sent DisconnectRequest");

        // Wait for DisconnectResponse
        try
        {
            var timeout = Task.Delay(5000);
            var readTask = ReadMessage(stream);
            var completedTask = await Task.WhenAny(readTask, timeout);

            if (completedTask == readTask)
            {
                var (msgType, msgData) = await readTask;
                if (msgType == 6) // DisconnectResponse
                    Console.WriteLine("Received DisconnectResponse");
            }
            else
            {
                Console.WriteLine("Disconnect timeout");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Disconnect error: {ex.Message}");
        }

        socket.Close();
        Console.WriteLine("Cleanup completed");
    }

    private static async Task SendHelloWorld(NetworkStream stream)
    {
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
    }

    private static async Task SendMessage(NetworkStream stream, uint messageType, IMessage message)
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

    private static async Task<(uint type, byte[] data)> ReadMessage(NetworkStream stream)
    {
        var header = new byte[3];
        await stream.ReadExactlyAsync(header);

        var length = header[1];
        var type = header[2];

        var data = new byte[length];
        await stream.ReadExactlyAsync(data);

        return (type, data);
    }
}