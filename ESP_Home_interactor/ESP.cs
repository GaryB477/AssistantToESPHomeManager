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
        await Authenticate(_stream);
        await ListAndSubscribeToEntities(_stream);
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

    private static async Task Authenticate(NetworkStream stream)
    {
        // Send AuthenticationRequest (empty password)
        var authRequest = new AuthenticationRequest();
        await SendMessage(stream, 3, authRequest);
        Console.WriteLine("Sent AuthenticationRequest");

        // Note: According to the protocol, the server may not send AuthenticationResponse
        // if password authentication is not required. We should just proceed.
    }

    private static async Task ListAndSubscribeToEntities(NetworkStream stream)
    {
        // Send ListEntitiesRequest
        var listEntitiesRequest = new ListEntitiesRequest();
        await SendMessage(stream, 11, listEntitiesRequest);
        Console.WriteLine("Sent ListEntitiesRequest");

        // Read entity list
        var switchEntities = new Dictionary<uint, string>();
        while (true)
        {
            try
            {
                var (msgType, msgData) = await ReadMessage(stream);

                if (msgType == 19) // ListEntitiesDoneResponse
                {
                    Console.WriteLine("Entity list complete");
                    break;
                }
                else if (msgType == 17) // ListEntitiesSwitchResponse
                {
                    var switchEntity = ListEntitiesSwitchResponse.Parser.ParseFrom(msgData);
                    switchEntities[switchEntity.Key] = switchEntity.Name;
                    Console.WriteLine($"Found switch: {switchEntity.Name} (key: {switchEntity.Key})");
                }
                // Ignore other entity types for now
            }
            catch (EndOfStreamException ex)
            {
                Console.WriteLine($"Connection closed during entity listing: {ex.Message}");
                break;
            }
        }

        if (switchEntities.Count == 0)
        {
            Console.WriteLine("No switch entities found");
            return;
        }

        // Subscribe to state updates
        var subscribeStatesRequest = new SubscribeStatesRequest();
        await SendMessage(stream, 20, subscribeStatesRequest);
        Console.WriteLine("Sent SubscribeStatesRequest");

        // Read initial states and updates for 5 seconds
        var timeout = Task.Delay(5000);
        Console.WriteLine("\nSwitch States:");
        Console.WriteLine("==============");

        while (!timeout.IsCompleted)
        {
            var readTask = ReadMessage(stream);
            var completedTask = await Task.WhenAny(readTask, timeout);

            if (completedTask == readTask)
            {
                try
                {
                    var (msgType, msgData) = await readTask;

                    if (msgType == 26) // SwitchStateResponse
                    {
                        var switchState = SwitchStateResponse.Parser.ParseFrom(msgData);
                        if (switchEntities.TryGetValue(switchState.Key, out var name))
                        {
                            var state = switchState.State ? "ON" : "OFF";
                            Console.WriteLine($"{name}: {state}");
                        }
                    }
                }
                catch (EndOfStreamException)
                {
                    Console.WriteLine("Connection closed by server");
                    break;
                }
            }
        }
    }

    private static async Task SendMessage(NetworkStream stream, uint messageType, IMessage message)
    {
        var data = message.ToByteArray();

        // Write preamble (0x00)
        await stream.WriteAsync(new byte[] { 0x00 });

        // Write message length as VarInt
        await WriteVarInt(stream, (uint)data.Length);

        // Write message type as VarInt
        await WriteVarInt(stream, messageType);

        // Write message data
        await stream.WriteAsync(data);
        await stream.FlushAsync();
    }

    private static async Task<(uint type, byte[] data)> ReadMessage(NetworkStream stream)
    {
        // Read preamble (0x00)
        var preamble = new byte[1];
        var bytesRead = await stream.ReadAsync(preamble);
        if (bytesRead == 0)
            throw new EndOfStreamException("Connection closed by remote host");

        if (preamble[0] != 0x00)
            throw new InvalidDataException($"Invalid preamble: expected 0x00, got 0x{preamble[0]:X2}");

        // Read message length as VarInt
        var length = await ReadVarInt(stream);

        // Read message type as VarInt
        var type = await ReadVarInt(stream);

        // Read message data
        var data = new byte[length];
        if (length > 0)
            await stream.ReadExactlyAsync(data);

        return (type, data);
    }

    private static async Task WriteVarInt(NetworkStream stream, uint value)
    {
        while (value >= 0x80)
        {
            await stream.WriteAsync(new byte[] { (byte)((value & 0x7F) | 0x80) });
            value >>= 7;
        }
        await stream.WriteAsync(new byte[] { (byte)value });
    }

    private static async Task<uint> ReadVarInt(NetworkStream stream)
    {
        uint result = 0;
        int shift = 0;

        while (true)
        {
            var buffer = new byte[1];
            await stream.ReadExactlyAsync(buffer);
            byte b = buffer[0];

            result |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                break;

            shift += 7;
            if (shift >= 32)
                throw new InvalidDataException("VarInt too long");
        }

        return result;
    }
}