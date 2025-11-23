using System.Net.Sockets;
using Google.Protobuf;

namespace ESP_Home_Interactor;

/// <summary>
/// High-level ESPHome API client
/// Handles device connection, entity discovery, and device control
/// </summary>
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
    private ESPHomeConnection? _connection;

    public async Task Init()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(Host, Port);
        var stream = new NetworkStream(socket);
        _connection = new ESPHomeConnection(socket, stream);
    }

    public async Task Run()
    {
        if (_connection == null) throw new InvalidOperationException("Connection not initialized");

        await SendHelloWorld(_connection);
        await Authenticate(_connection);
        var switches = await ListAndSubscribeToEntities(_connection);

        // Example: Toggle the first switch if any found
        if (switches.Count > 0)
        {
            var firstSwitch = switches.First();
            Console.WriteLine($"\n━━━ Testing Switch Control ━━━");

            // Turn ON
            await SetSwitchState(_connection, firstSwitch.Key, true, firstSwitch.Value);
            await ListenForStateUpdates(_connection, switches, 3000);

            // Turn OFF
            await SetSwitchState(_connection, firstSwitch.Key, false, firstSwitch.Value);
            await ListenForStateUpdates(_connection, switches, 3000);

            Console.WriteLine($"━━━ Test Complete ━━━\n");
        }

        // Drain any remaining messages before disconnect
        await DrainMessages(_connection, 500);

        await Cleanup(_connection);
    }

    private static async Task DrainMessages(ESPHomeConnection connection, int milliseconds)
    {
        Console.WriteLine($"Draining remaining messages...");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(milliseconds);
        int count = 0;

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                // Check if data is available before trying to read
                if (!connection.DataAvailable)
                {
                    await Task.Delay(50, cts.Token);
                    continue;
                }

                var (msgType, msgData) = await connection.ReadMessage();
                count++;
                Console.WriteLine($"  Drained message type: {msgType}");
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached - this is normal
        }
        catch (EndOfStreamException)
        {
            // Connection closed
        }

        if (count > 0)
        {
            Console.WriteLine($"Drained {count} pending message(s)\n");
        }
    }

    private static async Task Cleanup(ESPHomeConnection connection)
    {
        Console.WriteLine("━━━ Disconnecting ━━━");

        // Send DisconnectRequest
        var disconnectRequest = new DisconnectRequest();
        await connection.SendMessage(5, disconnectRequest);
        Console.WriteLine("→ Sent DisconnectRequest");

        // Wait for DisconnectResponse
        try
        {
            var timeout = Task.Delay(5000);
            var readTask = connection.ReadMessage();
            var completedTask = await Task.WhenAny(readTask, timeout);

            if (completedTask == readTask)
            {
                var (msgType, msgData) = await readTask;
                if (msgType == 6) // DisconnectResponse
                    Console.WriteLine("← Received DisconnectResponse");
            }
            else
            {
                Console.WriteLine("⚠ Disconnect timeout");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Disconnect error: {ex.Message}");
        }

        connection.Close();
        Console.WriteLine("✓ Connection closed\n");
    }

    private static async Task SendHelloWorld(ESPHomeConnection connection)
    {
        // Send HelloRequest
        var helloRequest = new HelloRequest
        {
            ClientInfo = "ESP_Home_interactor",
            ApiVersionMajor = 1,
            ApiVersionMinor = 13
        };

        await connection.SendMessage(1, helloRequest);
        Console.WriteLine("→ Sent HelloRequest");

        // Read HelloResponse
        var (msgType, msgData) = await connection.ReadMessage();

        if (msgType == 2) // HelloResponse
        {
            var helloResponse = HelloResponse.Parser.ParseFrom(msgData);
            Console.WriteLine($"← Received HelloResponse");
            Console.WriteLine($"  Server: {helloResponse.ServerInfo}");
            Console.WriteLine($"  API: {helloResponse.ApiVersionMajor}.{helloResponse.ApiVersionMinor}");
            Console.WriteLine($"  Name: {helloResponse.Name}");
        }
    }

    private static async Task Authenticate(ESPHomeConnection connection)
    {
        // Send AuthenticationRequest (empty password)
        var authRequest = new AuthenticationRequest();
        await connection.SendMessage(3, authRequest);
        Console.WriteLine("→ Sent AuthenticationRequest");

        // Read the response - according to ESPHome protocol, the server ALWAYS sends
        // AuthenticationResponse, even if password authentication is not required
        var (msgType, msgData) = await connection.ReadMessage();

        if (msgType == 4) // AuthenticationResponse
        {
            var authResponse = AuthenticationResponse.Parser.ParseFrom(msgData);
            if (authResponse.InvalidPassword)
            {
                throw new InvalidOperationException("Authentication failed: Invalid password");
            }
            Console.WriteLine("← Received AuthenticationResponse (authenticated)");
        }
        else
        {
            // Received a different message type - this shouldn't happen
            Console.WriteLine($"⚠ Warning: Expected AuthenticationResponse (4) but got message type {msgType}");
            throw new InvalidOperationException($"Unexpected message type after authentication: {msgType}");
        }
    }

    public static async Task SetSwitchState(ESPHomeConnection connection, uint key, bool state, string? name = null)
    {
        var switchCommand = new SwitchCommandRequest
        {
            Key = key,
            State = state
        };

        await connection.SendMessage(33, switchCommand);
        var stateName = state ? "ON" : "OFF";
        var entityName = name != null ? $"'{name}' " : "";
        Console.WriteLine($"→ Sent SwitchCommand: {entityName}→ {stateName}");
    }

    private static async Task ListenForStateUpdates(ESPHomeConnection connection, Dictionary<uint, string> switches, int milliseconds)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(milliseconds);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                // Check if data is available before trying to read
                if (!connection.DataAvailable)
                {
                    await Task.Delay(50, cts.Token);
                    continue;
                }

                var (msgType, msgData) = await connection.ReadMessage();

                if (msgType == 26) // SwitchStateResponse
                {
                    var switchState = SwitchStateResponse.Parser.ParseFrom(msgData);
                    if (switches.TryGetValue(switchState.Key, out var name))
                    {
                        var state = switchState.State ? "ON" : "OFF";
                        Console.WriteLine($"← SwitchState: '{name}' is {state}");
                    }
                }
                else
                {
                    // Log other message types for debugging
                    Console.WriteLine($"← Received message type: {msgType}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached - this is normal
        }
        catch (EndOfStreamException)
        {
            Console.WriteLine("✗ Connection closed by server");
        }
    }

    private static async Task<Dictionary<uint, string>> ListAndSubscribeToEntities(ESPHomeConnection connection)
    {
        // Send ListEntitiesRequest
        var listEntitiesRequest = new ListEntitiesRequest();
        await connection.SendMessage(11, listEntitiesRequest);
        Console.WriteLine("\n→ Sent ListEntitiesRequest");

        // Read entity list
        var switchEntities = new Dictionary<uint, string>();
        while (true)
        {
            try
            {
                var (msgType, msgData) = await connection.ReadMessage();

                if (msgType == 19) // ListEntitiesDoneResponse
                {
                    Console.WriteLine($"← Received ListEntitiesDoneResponse ({switchEntities.Count} switches found)");
                    break;
                }
                else if (msgType == 17) // ListEntitiesSwitchResponse
                {
                    var switchEntity = ListEntitiesSwitchResponse.Parser.ParseFrom(msgData);
                    switchEntities[switchEntity.Key] = switchEntity.Name;
                    Console.WriteLine($"← Found switch: '{switchEntity.Name}' (key: {switchEntity.Key})");
                }
                else
                {
                    // Log ignored entity types for debugging
                    Console.WriteLine($"← Ignoring entity type {msgType} ({msgData.Length} bytes)");
                }
            }
            catch (EndOfStreamException ex)
            {
                Console.WriteLine($"✗ Connection closed during entity listing: {ex.Message}");
                break;
            }
        }

        if (switchEntities.Count == 0)
        {
            Console.WriteLine("⚠ No switch entities found");
            return switchEntities;
        }

        // Subscribe to state updates
        var subscribeStatesRequest = new SubscribeStatesRequest();
        await connection.SendMessage(20, subscribeStatesRequest);
        Console.WriteLine("\n→ Sent SubscribeStatesRequest");

        // Read initial states briefly to show current state
        Console.WriteLine("Waiting for initial state updates...");
        await ListenForStateUpdates(connection, switchEntities, 1000);

        return switchEntities;
    }
}
