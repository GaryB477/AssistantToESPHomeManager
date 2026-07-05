using System.Net.Sockets;
using ESP_Home_Interactor.Entities;
using ESP_Home_Interactor.helper;

namespace ESP_Home_Interactor;

/// <summary>
/// High-level ESPHome API client
/// Handles device connection, entity discovery, and device control.
/// Runs a persistent read loop that dispatches incoming messages
/// (ping requests, state updates, entity listings) by message type.
/// </summary>
public class EspBase(ESPConfig config)
{
    private readonly Logger _logger = new Logger();
    private readonly string _password = config.Password ?? "";

    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;
    private TaskCompletionSource<bool>? _entitiesDone;
    private TaskCompletionSource<bool> _disconnected = new();

    public int Port { get; set; } = config.Port;
    public string Host { get; set; } = config.Host;
    public ESPHomeConnection? Connection { get; private set; }
    public bool IsConnected => Connection != null;

    public List<SensorEntity> SensorEntities { get; private set; } = new List<SensorEntity>();
    public List<BinarySensorEntity> BinarySensorEntities { get; private set; } = new List<BinarySensorEntity>();
    public List<SwitchEntity> SwitchEntities { get; private set; } = new List<SwitchEntity>();
    public List<LightEntity> LightEntities { get; private set; } = new List<LightEntity>();

    /// <summary>Raised whenever an entity state update was received</summary>
    public event Action? StateChanged;

    /// <summary>Raised for every Bluetooth proxy message (advertisements, GATT responses, ...)</summary>
    public event Action<MessageType, byte[]>? BluetoothMessageReceived;

    public async Task Init()
    {
        await InitConnection();
        StartReadLoop();
        await FetchAllEntities();
        await SubscribeStates();
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
            _disconnected = new TaskCompletionSource<bool>();
            await SendHelloWorld(Connection);
            await Authenticate(Connection);
        }
        catch (OperationCanceledException)
        {
            Connection = null;
            throw new TimeoutException($"Connection to {Host}:{Port} timed out after {timeoutMilliseconds}ms");
        }
        catch
        {
            Connection = null;
            throw;
        }
    }

    /// <summary>
    /// Completes when the connection is lost or closed. Used by the device
    /// service to trigger reconnects.
    /// </summary>
    public Task WaitForDisconnect(CancellationToken cancellationToken) =>
        _disconnected.Task.WaitAsync(cancellationToken);

    private void StartReadLoop()
    {
        _readLoopCts = new CancellationTokenSource();
        var token = _readLoopCts.Token;
        _readLoopTask = Task.Run(() => ReadLoop(token));
    }

    private async Task ReadLoop(CancellationToken token)
    {
        var connection = Connection!;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var (msgType, msgData) = await connection.ReadMessage();
                await HandleMessage(connection, (MessageType)msgType, msgData);
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException or SocketException)
        {
            if (!token.IsCancellationRequested)
                _logger.LogWarning($"[{Host}] Connection lost: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[{Host}] Read loop error: {ex.Message}");
        }
        finally
        {
            Connection = null;
            _entitiesDone?.TrySetException(new EndOfStreamException($"[{Host}] Connection closed"));
            _disconnected.TrySetResult(true);
        }
    }

    private async Task HandleMessage(ESPHomeConnection connection, MessageType msgType, byte[] msgData)
    {
        switch (msgType)
        {
            case MessageType.PingRequest:
                await connection.SendMessage((uint)MessageType.PingResponse, new PingResponse());
                break;

            case MessageType.DisconnectRequest:
                await connection.SendMessage((uint)MessageType.DisconnectResponse, new DisconnectResponse());
                connection.Close();
                break;

            case MessageType.ListEntitiesSensorResponse:
            {
                var sensorEntity = ListEntitiesSensorResponse.Parser.ParseFrom(msgData);
                SensorEntities.Add(new SensorEntity(sensorEntity.Key, sensorEntity.Name, sensorEntity.ObjectId,
                    sensorEntity.UnitOfMeasurement, sensorEntity.AccuracyDecimals));
                _logger.LogIncoming($"Found sensor: '{sensorEntity.Name}' (key: {sensorEntity.Key})");
                break;
            }

            case MessageType.ListEntitiesBinarySensorResponse:
            {
                var binarySensorEntity = ListEntitiesBinarySensorResponse.Parser.ParseFrom(msgData);
                BinarySensorEntities.Add(new BinarySensorEntity(binarySensorEntity.Key, binarySensorEntity.Name,
                    binarySensorEntity.ObjectId));
                _logger.LogIncoming($"Found binary sensor: '{binarySensorEntity.Name}' (key: {binarySensorEntity.Key})");
                break;
            }

            case MessageType.ListEntitiesSwitchResponse:
            {
                var switchEntity = ListEntitiesSwitchResponse.Parser.ParseFrom(msgData);
                SwitchEntities.Add(new SwitchEntity(switchEntity.Key, switchEntity.Name, switchEntity.ObjectId));
                _logger.LogIncoming($"Found switch: '{switchEntity.Name}' (key: {switchEntity.Key})");
                break;
            }

            case MessageType.ListEntitiesLightResponse:
            {
                var lightEntity = ListEntitiesLightResponse.Parser.ParseFrom(msgData);
                // COLOR_MODE_BRIGHTNESS bit (2) covers modern configs, legacy flag older firmwares
                var supportsBrightness = lightEntity.LegacySupportsBrightness ||
                                         lightEntity.SupportedColorModes.Any(m => ((int)m & 2) != 0);
                LightEntities.Add(new LightEntity(lightEntity.Key, lightEntity.Name, lightEntity.ObjectId,
                    supportsBrightness));
                _logger.LogIncoming($"Found light: '{lightEntity.Name}' (key: {lightEntity.Key})");
                break;
            }

            case MessageType.ListEntitiesDoneResponse:
                _entitiesDone?.TrySetResult(true);
                break;

            case MessageType.SensorStateResponse:
            {
                var state = SensorStateResponse.Parser.ParseFrom(msgData);
                UpdateEntityState(SensorEntities.FirstOrDefault(e => e.Key == state.Key), msgData);
                break;
            }

            case MessageType.BinarySensorStateResponse:
            {
                var state = BinarySensorStateResponse.Parser.ParseFrom(msgData);
                UpdateEntityState(BinarySensorEntities.FirstOrDefault(e => e.Key == state.Key), msgData);
                break;
            }

            case MessageType.SwitchStateResponse:
            {
                var state = SwitchStateResponse.Parser.ParseFrom(msgData);
                UpdateEntityState(SwitchEntities.FirstOrDefault(e => e.Key == state.Key), msgData);
                break;
            }

            case MessageType.LightStateResponse:
            {
                var state = LightStateResponse.Parser.ParseFrom(msgData);
                UpdateEntityState(LightEntities.FirstOrDefault(e => e.Key == state.Key), msgData);
                break;
            }

            default:
                if (IsBluetoothMessage(msgType))
                {
                    BluetoothMessageReceived?.Invoke(msgType, msgData);
                    break;
                }
                _logger.Log($"[{Host}] Ignoring message type {msgType} ({msgData.Length} bytes)");
                break;
        }
    }

    private static bool IsBluetoothMessage(MessageType msgType)
    {
        var id = (uint)msgType;
        return (id >= 66 && id <= 88) || msgType == MessageType.BluetoothLERawAdvertisementsResponse;
    }

    private void UpdateEntityState<T>(EntityBase<T>? entity, byte[] msgData)
    {
        if (entity == null) return;
        entity.UpdateState(msgData);
        StateChanged?.Invoke();
    }

    public async Task Cleanup()
    {
        if (Connection == null) return;

        _logger.LogSeparator($"Disconnecting from {Host}");

        try
        {
            await Connection.SendMessage((uint)MessageType.DisconnectRequest, new DisconnectRequest());
            _logger.LogOutgoing("Sent DisconnectRequest");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Disconnect error: {ex.Message}");
        }

        _readLoopCts?.Cancel();
        Connection?.Close();

        if (_readLoopTask != null)
            await _readLoopTask;

        _logger.LogSuccess("Connection closed");
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
        var authRequest = new AuthenticationRequest { Password = _password };
        await connection.SendMessage((uint)MessageType.AuthenticationRequest, authRequest);
        _logger.LogOutgoing("Sent AuthenticationRequest");

        // The device replies with an AuthenticationResponse (invalid_password flag).
        // Some firmwares skip the response when no password is configured, so a
        // short timeout counts as success.
        using var cts = new CancellationTokenSource(2000);

        try
        {
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

                    _logger.LogWarning($"Got message type {msgType} after auth request - assuming auth succeeded");
                    return;
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

    public async Task FetchAllEntities(int timeoutMilliseconds = 10000)
    {
        if (Connection == null) throw new InvalidOperationException("Connection not initialized");

        SensorEntities.Clear();
        BinarySensorEntities.Clear();
        SwitchEntities.Clear();
        LightEntities.Clear();

        _entitiesDone = new TaskCompletionSource<bool>();

        await Connection.SendMessage((uint)MessageType.ListEntitiesRequest, new ListEntitiesRequest());
        _logger.LogOutgoing("Sent ListEntitiesRequest");

        await _entitiesDone.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));

        var entitySum = SensorEntities.Count + BinarySensorEntities.Count +
                        SwitchEntities.Count + LightEntities.Count;
        _logger.LogIncoming($"Received ListEntitiesDoneResponse ({entitySum} entities found)");
    }

    private async Task SubscribeStates()
    {
        if (Connection == null) throw new InvalidOperationException("Connection not initialized");

        await Connection.SendMessage((uint)MessageType.SubscribeStatesRequest, new SubscribeStatesRequest());
        _logger.LogOutgoing("Sent SubscribeStatesRequest");
    }
}
