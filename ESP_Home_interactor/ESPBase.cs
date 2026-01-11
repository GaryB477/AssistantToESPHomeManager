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
    public ESPHomeConnection? Connection;
    public List<SensorEntity> SensorEntities { get; private set; } = new List<SensorEntity>();
    public List<BinarySensorEntity> BinarySensorEntities { get; private set; } = new List<BinarySensorEntity>();
    public List<SwitchEntity> SwitchEntities { get; private set; } = new List<SwitchEntity>();

    public async Task Init()
    {
        await InitConnection();
        await FetchAllSensorEntities();
    }

    public async Task InitConnection(int timeoutMilliseconds = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMilliseconds);

        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(Host, Port, cts.Token);
            var stream = new NetworkStream(socket);
            Connection = new ESPHomeConnection(socket, stream);
            await SendHelloWorld(Connection);
            await Authenticate(Connection);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Connection to {Host}:{Port} timed out after {timeoutMilliseconds}ms");
        }
    }

    public async Task Sync()
    {
        if (Connection == null) throw new InvalidOperationException("Connection not initialized");
        if ((SensorEntities.Count +
             BinarySensorEntities.Count +
             SwitchEntities.Count) == 0) throw new InvalidOperationException("No sensors found");

        await UpdateAllSensorStates();

        await DrainMessages(Connection, 500);
    }

    private async Task UpdateAllSensorStates(int timeoutMilliseconds = 2000)
    {
        if (Connection == null) throw new InvalidOperationException("Connection not initialized");

        _logger.LogEmpty();
        _logger.LogSeparator("Updating Sensor States");

        // Send SubscribeStatesRequest
        var subscribeRequest = new SubscribeStatesRequest();
        await Connection.SendMessage((uint)MessageType.SubscribeStatesRequest, subscribeRequest);
        _logger.LogOutgoing("Sent SubscribeStatesRequest");

        using var cts = new CancellationTokenSource(timeoutMilliseconds);
        int updatedCount = 0;

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                // Check if data is available before trying to read
                if (!Connection.DataAvailable)
                {
                    await Task.Delay(50, cts.Token);
                    continue;
                }

                var (msgType, msgData) = await Connection.ReadMessage();

                switch (msgType)
                {
                    case (uint)MessageType.SwitchStateResponse:
                    {
                        var state = SwitchStateResponse.Parser.ParseFrom(msgData);
                        var entity = SwitchEntities.FirstOrDefault(e => e.Key == state.Key);
                        if (entity != null)
                        {
                            entity.UpdateState(msgData);
                            updatedCount++;
                        }

                        break;
                    }
                    case (uint)MessageType.SensorStateResponse:
                    {
                        var state = SensorStateResponse.Parser.ParseFrom(msgData);
                        var entity = SensorEntities.FirstOrDefault(e => e.Key == state.Key);
                        if (entity != null)
                        {
                            entity.UpdateState(msgData);
                            updatedCount++;
                        }

                        break;
                    }
                    case (uint)MessageType.BinarySensorStateResponse:
                    {
                        var state = BinarySensorStateResponse.Parser.ParseFrom(msgData);
                        var entity = BinarySensorEntities.FirstOrDefault(e => e.Key == state.Key);
                        if (entity != null)
                        {
                            entity.UpdateState(msgData);
                            updatedCount++;
                        }

                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached - this is normal
        }
        catch (EndOfStreamException)
        {
            _logger.LogWarning("Connection closed during state update");
        }

        _logger.LogSuccess($"Updated {updatedCount} sensor state(s)");
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

    public async Task Cleanup()
    {
        _logger.LogSeparator("Disconnecting");

        // Send DisconnectRequest
        var disconnectRequest = new DisconnectRequest();
        if (Connection == null) throw new InvalidOperationException("Connection not initialized");
        await Connection?.SendMessage((uint)MessageType.DisconnectRequest, disconnectRequest)!;
        _logger.LogOutgoing("Sent DisconnectRequest");

        // Wait for DisconnectResponse
        try
        {
            var timeout = Task.Delay(5000);
            var readTask = Connection.ReadMessage();
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

        Connection.Close();
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

        // IMPORTANT: According to ESPHome protocol, the device will ONLY send
        // AuthenticationResponse if authentication FAILS. If authentication succeeds
        // or is not required, the device will NOT send a response - it will just
        // continue with normal operation (sending pings, etc.)
        //
        // Therefore, we check if there's an immediate response. If we get an
        // AuthenticationResponse, it means auth failed. Otherwise, we assume success.

        using var cts = new CancellationTokenSource(1000); // Wait up to 1 second

        try
        {
            // Check if there's data available with a short timeout
            while (!cts.Token.IsCancellationRequested)
            {
                if (connection.DataAvailable)
                {
                    var (msgType, msgData) = await connection.ReadMessage();

                    if (msgType == (uint)MessageType.AuthenticationResponse)
                    {
                        var authResponse = AuthenticationResponse.Parser.ParseFrom(msgData);
                        if (authResponse.InvalidPassword)
                        {
                            throw new InvalidOperationException("Authentication failed: Invalid password");
                        }
                        _logger.LogIncoming("Received AuthenticationResponse (authenticated)");
                        return;
                    }
                    else
                    {
                        // Got some other message - auth must have succeeded, but we need to
                        // handle this message. For now, just log it as unexpected
                        _logger.LogWarning($"Got message type {msgType} after auth request - assuming auth succeeded");
                        return;
                    }
                }

                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout - no response means authentication succeeded
        }

        _logger.LogIncoming("No AuthenticationResponse received - authentication succeeded");
    }

    public async Task FetchAllSensorEntities()
    {
        if (Connection == null) throw new InvalidOperationException("Connection not initialized");
        // Send ListEntitiesRequest
        var listEntitiesRequest = new ListEntitiesRequest();
        await Connection.SendMessage((uint)MessageType.ListEntitiesRequest, listEntitiesRequest);
        _logger.LogEmpty();
        _logger.LogOutgoing("Sent ListEntitiesRequest");

        while (true)
        {
            try
            {
                var (msgType, msgData) = await Connection.ReadMessage();

                // Abort if end is reached
                if (msgType == (uint)MessageType.ListEntitiesDoneResponse)
                {
                    var sensorSum = SensorEntities.Count + BinarySensorEntities.Count + SwitchEntities.Count;
                    _logger.LogIncoming($"Received ListEntitiesDoneResponse ({sensorSum} sensors found)");
                    break;
                }

                switch (msgType)
                {
                    case (uint)MessageType.ListEntitiesSwitchResponse:
                        var switchEntity = ListEntitiesSwitchResponse.Parser.ParseFrom(msgData);
                        SwitchEntities.Add(new SwitchEntity(switchEntity.Key, switchEntity.Name,
                            switchEntity.ObjectId));
                        _logger.LogIncoming($"Found sensor: '{switchEntity.Name}' (key: {switchEntity.Key})");
                        break;
                    case (uint)MessageType.ListEntitiesSensorResponse:
                    {
                        var sensorEntity = ListEntitiesSensorResponse.Parser.ParseFrom(msgData);
                        SensorEntities.Add(new SensorEntity(sensorEntity.Key, sensorEntity.Name, sensorEntity.ObjectId,
                            sensorEntity.UnitOfMeasurement, sensorEntity.AccuracyDecimals));
                        _logger.LogIncoming($"Found sensor: '{sensorEntity.Name}' (key: {sensorEntity.Key})");
                        break;
                    }
                    case (uint)MessageType.ListEntitiesBinarySensorResponse:
                    {
                        var binarySensorEntity = ListEntitiesBinarySensorResponse.Parser.ParseFrom(msgData);
                        BinarySensorEntities.Add(new BinarySensorEntity(binarySensorEntity.Key, binarySensorEntity.Name,
                            binarySensorEntity.ObjectId));
                        _logger.LogIncoming(
                            $"Found binary sensor: '{binarySensorEntity.Name}' (key: {binarySensorEntity.Key})");
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
    }
}