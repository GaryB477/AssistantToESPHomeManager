using System.Net.Sockets;
using ESP_Home_Interactor.Entities;
using ESP_Home_Interactor.helper;
using Google.Protobuf.WellKnownTypes;

namespace ESP_Home_Interactor;

/// <summary>
/// High-level ESPHome API client
/// Handles device connection, entity discovery, and device control
/// </summary>
public class EspBase(ESPConfig config)
{
    private readonly Logger _logger = new Logger();

    public int Port { get; set; } = config.Port;
    public string Host { get; set; } = config.Host;
    private ESPHomeConnection? _connection;

    public async Task Init()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(Host, Port);
        var stream = new NetworkStream(socket);
        _connection = new ESPHomeConnection(socket, stream);
        await SendHelloWorld(_connection);
        await Authenticate(_connection);
    }

    public async Task Run()
    {
        if (_connection == null) throw new InvalidOperationException("Connection not initialized");
        // var switches = await ListAndSubscribeToEntities(_connection);

        // Example: Toggle the first switch if any found
        // if (switches.Count > 0)
        {
            // var firstSwitch = switches.First();
            // _logger.LogEmpty();
            // _logger.LogSeparator("Testing Switch Control");

            // Turn ON
            // await SetSwitchState(_connection, firstSwitch.Key, true, firstSwitch.Value);
            // await ListenForStateUpdates(_connection, switches, 3000);

            // Turn OFF
            // await SetSwitchState(_connection, firstSwitch.Key, false, firstSwitch.Value);
            // await ListenForStateUpdates(_connection, switches, 3000);

            // _logger.LogSeparator("Test Complete");
            // _logger.LogEmpty();
        }

        // Drain any remaining messages before disconnect
        await DrainMessages(_connection, 500);

        await Cleanup(_connection);
    }

    private async Task DrainMessages(ESPHomeConnection connection, int milliseconds)
    {
        _logger.Log($"Draining remaining messages...");
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
                _logger.Log($"  Drained message type: {msgType}");
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
            _logger.Log($"Drained {count} pending message(s)\n");
        }
    }

    private async Task Cleanup(ESPHomeConnection connection)
    {
        _logger.LogSeparator("Disconnecting");

        // Send DisconnectRequest
        var disconnectRequest = new DisconnectRequest();
        await connection.SendMessage((uint)MessageType.DisconnectRequest, disconnectRequest);
        _logger.LogOutgoing("Sent DisconnectRequest");

        // Wait for DisconnectResponse
        try
        {
            var timeout = Task.Delay(5000);
            var readTask = connection.ReadMessage();
            var completedTask = await Task.WhenAny(readTask, timeout);

            if (completedTask == readTask)
            {
                var (msgType, msgData) = await readTask;
                if (msgType == (uint)MessageType.DisconnectResponse)
                    _logger.LogIncoming("Received DisconnectResponse");
            }
            else
            {
                _logger.LogWarning("Disconnect timeout");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Disconnect error: {ex.Message}");
        }

        connection.Close();
        _logger.LogSuccess("Connection closed");
        _logger.LogEmpty();
    }

    private async Task SendHelloWorld(ESPHomeConnection connection)
    {
        // Send HelloRequest
        var helloRequest = new HelloRequest
        {
            ClientInfo = "ESP_Home_interactor",
            ApiVersionMajor = 1,
            ApiVersionMinor = 13
        };

        await connection.SendMessage((uint)MessageType.HelloRequest, helloRequest);
        _logger.LogOutgoing("Sent HelloRequest");

        // Read HelloResponse
        var (msgType, msgData) = await connection.ReadMessage();

        if (msgType == (uint)MessageType.HelloResponse)
        {
            var helloResponse = HelloResponse.Parser.ParseFrom(msgData);
            _logger.LogIncoming($"Received HelloResponse");
            _logger.Log($"  Server: {helloResponse.ServerInfo}");
            _logger.Log($"  API: {helloResponse.ApiVersionMajor}.{helloResponse.ApiVersionMinor}");
            _logger.Log($"  Name: {helloResponse.Name}");
        }
    }

    private async Task Authenticate(ESPHomeConnection connection)
    {
        // Send AuthenticationRequest (empty password)
        var authRequest = new AuthenticationRequest();
        await connection.SendMessage((uint)MessageType.AuthenticationRequest, authRequest);
        _logger.LogOutgoing("Sent AuthenticationRequest");

        // Read the response - according to ESPHome protocol, the server ALWAYS sends
        // AuthenticationResponse, even if password authentication is not required
        var (msgType, msgData) = await connection.ReadMessage();

        if (msgType == (uint)MessageType.AuthenticationResponse)
        {
            var authResponse = AuthenticationResponse.Parser.ParseFrom(msgData);
            if (authResponse.InvalidPassword)
            {
                throw new InvalidOperationException("Authentication failed: Invalid password");
            }
            _logger.LogIncoming("Received AuthenticationResponse (authenticated)");
        }
        else
        {
            // Received a different message type - this shouldn't happen
            _logger.LogWarning($"Expected AuthenticationResponse (4) but got message type {msgType}");
            throw new InvalidOperationException($"Unexpected message type after authentication: {msgType}");
        }
    }

    public async Task SetSwitchState(ESPHomeConnection connection, uint key, bool state, string? name = null)
    {
        var switchCommand = new SwitchCommandRequest
        {
            Key = key,
            State = state
        };

        await connection.SendMessage((uint)MessageType.SwitchCommandRequest, switchCommand);
        var stateName = state ? "ON" : "OFF";
        var entityName = name != null ? $"'{name}' " : "";
        _logger.LogOutgoing($"Sent SwitchCommand: {entityName}→ {stateName}");
    }

    private async Task ListenForStateUpdates(ESPHomeConnection connection, Dictionary<uint, string> switches, int milliseconds)
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

                if (msgType == (uint)MessageType.SwitchStateResponse)
                {
                    var switchState = SwitchStateResponse.Parser.ParseFrom(msgData);
                    if (switches.TryGetValue(switchState.Key, out var name))
                    {
                        var state = switchState.State ? "ON" : "OFF";
                        _logger.LogIncoming($"SwitchState: '{name}' is {state}");
                    }
                }
                else
                {
                    // Log other message types for debugging
                    _logger.LogIncoming($"Received message type: {msgType}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached - this is normal
        }
        catch (EndOfStreamException)
        {
            _logger.LogError("Connection closed by server");
        }
    }

    private async Task ListAndSubscribeToEntities(ESPHomeConnection connection)
    {
        // Subscribe to state updates
        var subscribeStatesRequest = new SubscribeStatesRequest();
        await connection.SendMessage((uint)MessageType.SubscribeStatesRequest, subscribeStatesRequest);
        _logger.LogEmpty();
        _logger.LogOutgoing("Sent SubscribeStatesRequest");

        // Read initial states briefly to show current state
        _logger.Log("Waiting for initial state updates...");
        // await ListenForStateUpdates(connection, switchEntities, 1000);
        // return switchEntities;
    }

    public async Task<List<object>> GetAllSensorEntities()
    {
        if (_connection == null) throw new InvalidOperationException("Connection not initialized");
        // Send ListEntitiesRequest
        var listEntitiesRequest = new ListEntitiesRequest();
        await _connection.SendMessage((uint)MessageType.ListEntitiesRequest, listEntitiesRequest);
        _logger.LogEmpty();
        _logger.LogOutgoing("Sent ListEntitiesRequest");

        // Read entity list
        var sensorEntities = new List<object>();
        while (true)
        {
            try
            {
                var (msgType, msgData) = await _connection.ReadMessage();

                // Abort if end is reached
                if (msgType == (uint)MessageType.ListEntitiesDoneResponse)
                {
                    _logger.LogIncoming($"Received ListEntitiesDoneResponse ({sensorEntities.Count} sensors found)");
                    break;
                }
                
                switch (msgType)
                {
                    case (uint)MessageType.ListEntitiesSwitchResponse:
                        var switchEntity = ListEntitiesSwitchResponse.Parser.ParseFrom(msgData);
                        sensorEntities.Add(new SwitchEntity(switchEntity.Key, switchEntity.Name, switchEntity.ObjectId));
                        _logger.LogIncoming($"Found sensor: '{switchEntity.Name}' (key: {switchEntity.Key})");
                        break;
                        
                    case (uint)MessageType.ListEntitiesSensorResponse:
                    {
                        var sensorEntity = ListEntitiesSensorResponse.Parser.ParseFrom(msgData);
                        sensorEntities.Add(new SensorEntity(sensorEntity.Key, sensorEntity.Name, sensorEntity.ObjectId, 
                            sensorEntity.UnitOfMeasurement, sensorEntity.AccuracyDecimals));
                        _logger.LogIncoming($"Found sensor: '{sensorEntity.Name}' (key: {sensorEntity.Key})");
                        break;
                    }
                    case (uint)MessageType.ListEntitiesBinarySensorResponse:
                    {
                        var binarySensorEntity = ListEntitiesBinarySensorResponse.Parser.ParseFrom(msgData);
                        sensorEntities.Add(new BinarySensorEntity(binarySensorEntity.Key, binarySensorEntity.Name, binarySensorEntity.ObjectId));
                        _logger.LogIncoming($"Found binary sensor: '{binarySensorEntity.Name}' (key: {binarySensorEntity.Key})");
                        break;
                    }
                    default:
                        // Log ignored entity types for debugging
                        _logger.LogIncoming($"Ignoring entity type {msgType} ({msgData.Length} bytes)");
                        break;
                }
            }
            catch (EndOfStreamException ex)
            {
                _logger.LogError($"Connection closed during entity listing: {ex.Message}");
                break;
            }
        }

        if (sensorEntities.Count == 0)
        {
            _logger.LogWarning("No sensor entities found");
        }
        return sensorEntities;

        // Subscribe to state updates
        var subscribeStatesRequest = new SubscribeStatesRequest();
        await _connection.SendMessage((uint)MessageType.SubscribeStatesRequest, subscribeStatesRequest);
        _logger.LogEmpty();
        _logger.LogOutgoing("Sent SubscribeStatesRequest");

        // Read initial states briefly to show current state
        // _logger.Log("Waiting for initial sensor state updates...");
        // await ListenForSensorStateUpdates(connection, sensorEntities, 1000);

        return sensorEntities;
    }
}
