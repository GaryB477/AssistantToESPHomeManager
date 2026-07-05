using ESP_Home_Interactor.helper;
using Google.Protobuf;

namespace ESP_Home_Interactor.AcInfinity;

/// <summary>
/// GATT client for a single BLE device behind an ESPHome Bluetooth proxy.
/// Uses the proxy's persistent API connection (EspBase read loop) and
/// correlates request/response pairs via TaskCompletionSources.
/// </summary>
public class BleProxyClient
{
    private readonly EspBase _proxy;
    private readonly Logger _logger = new();

    private TaskCompletionSource<bool>? _connectTcs;
    private TaskCompletionSource<bool>? _disconnectTcs;
    private TaskCompletionSource<bool>? _servicesDoneTcs;
    private TaskCompletionSource<bool>? _notifyEnableTcs;
    private TaskCompletionSource<bool>? _writeTcs;
    private TaskCompletionSource<byte[]>? _notificationTcs;
    private readonly List<BluetoothGATTService> _services = new();

    private static readonly string[] WriteCharacteristicUuids =
    {
        "70D510012C7F4E75AE8AD758951CE4E0",
        "0000FF0100001000800000805F9B34FB"
    };

    private static readonly string[] NotifyCharacteristicUuids =
    {
        "70D510022C7F4E75AE8AD758951CE4E0",
        "0000FF0200001000800000805F9B34FB"
    };

    public ulong Address { get; }
    public string Mac { get; }
    public bool DeviceConnected { get; private set; }
    public uint WriteHandle { get; private set; }
    public uint NotifyHandle { get; private set; }

    /// <summary>Raw AC Infinity manufacturer payload seen in an advertisement of this device</summary>
    public event Action<byte[]>? AdvertisementReceived;

    public BleProxyClient(EspBase proxy, string mac)
    {
        _proxy = proxy;
        Mac = mac;
        Address = MacToUint64(mac);
        _proxy.BluetoothMessageReceived += OnBluetoothMessage;
    }

    public static ulong MacToUint64(string mac)
    {
        var parts = mac.Split(':');
        if (parts.Length != 6)
            throw new ArgumentException($"Invalid MAC address: {mac}", nameof(mac));

        return parts.Aggregate(0UL, (acc, part) => (acc << 8) | Convert.ToByte(part, 16));
    }

    /// <summary>
    /// Subscribe to raw BLE advertisements on the proxy (flags = 1: raw mode).
    /// Also subscribes to the free-connection-slot updates: the proxy only
    /// processes BluetoothDeviceRequest (connect) from clients that hold BOTH
    /// subscriptions - without them the connect request is silently ignored
    /// (verified live: no response at all, not even an error).
    /// Both subscriptions must stay open for the lifetime of the connection.
    /// </summary>
    public async Task SubscribeAdvertisements()
    {
        await Send(MessageType.SubscribeBluetoothLEAdvertisementsRequest,
            new SubscribeBluetoothLEAdvertisementsRequest { Flags = 1 });
        await Send(MessageType.SubscribeBluetoothConnectionsFreeRequest,
            new SubscribeBluetoothConnectionsFreeRequest());
        _logger.LogOutgoing($"[BLE {Mac}] Subscribed to advertisements + connection slots");
    }

    /// <summary>
    /// Connect to the device, discover services and enable notifications.
    /// </summary>
    public async Task ConnectAsync(int timeoutMilliseconds = 20000)
    {
        if (DeviceConnected) return;

        _connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Send(MessageType.BluetoothDeviceRequest, new BluetoothDeviceRequest
        {
            Address = Address,
            RequestType = BluetoothDeviceRequestType.ConnectV3WithoutCache,
            HasAddressType = true,
            AddressType = 0
        });
        _logger.LogOutgoing($"[BLE {Mac}] Connect request sent");

        await _connectTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        _logger.LogIncoming($"[BLE {Mac}] Device connected");

        await DiscoverServices();
        await EnableNotifications();
    }

    private async Task DiscoverServices(int timeoutMilliseconds = 15000)
    {
        _services.Clear();
        _servicesDoneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Send(MessageType.BluetoothGATTGetServicesRequest,
            new BluetoothGATTGetServicesRequest { Address = Address });

        await _servicesDoneTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));

        // Candidate order matters: the reference implementation prefers the
        // 70D51001/70D51002 pair over the FF01/FF02 pair when both exist
        WriteHandle = ResolveHandle(WriteCharacteristicUuids);
        NotifyHandle = ResolveHandle(NotifyCharacteristicUuids);

        var dump = string.Join("\n", _services.Select(s =>
            $"  service {UuidToHex(s.Uuid, 0)}: " + string.Join(", ",
                s.Characteristics.Select(c =>
                    $"{UuidToHex(c.Uuid, c.ShortUuid)} (handle {c.Handle}, props 0x{c.Properties:X2})"))));
        _logger.Log($"[BLE {Mac}] GATT services:\n{dump}");

        if (WriteHandle == 0 || NotifyHandle == 0)
        {
            throw new InvalidOperationException($"[BLE {Mac}] Write/notify characteristics not found");
        }

        _logger.LogSuccess($"[BLE {Mac}] Resolved handles: write={WriteHandle}, notify={NotifyHandle}");
    }

    private uint ResolveHandle(string[] candidateUuids)
    {
        foreach (var candidate in candidateUuids)
        {
            foreach (var service in _services)
            {
                var characteristic = service.Characteristics
                    .FirstOrDefault(c => UuidToHex(c.Uuid, c.ShortUuid) == candidate);
                if (characteristic != null)
                    return characteristic.Handle;
            }
        }

        return 0;
    }

    private async Task EnableNotifications()
    {
        // Enable notify on every characteristic that supports it (props bit 0x10) -
        // the answer channel differs between firmware variants
        var notifyHandles = _services
            .SelectMany(s => s.Characteristics)
            .Where(c => (c.Properties & 0x10) != 0)
            .Select(c => c.Handle)
            .ToList();

        foreach (var handle in notifyHandles)
        {
            _notifyEnableTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await Send(MessageType.BluetoothGATTNotifyRequest, new BluetoothGATTNotifyRequest
            {
                Address = Address,
                Handle = handle,
                Enable = true
            });

            // Wait for the CCCD write confirmation - writing a command before the
            // subscription is active loses the device's response notification
            try
            {
                await _notifyEnableTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(3000));
                _logger.LogOutgoing($"[BLE {Mac}] Notifications enabled (handle {handle})");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning($"[BLE {Mac}] No notify confirmation for handle {handle} - continuing");
            }
        }
    }

    /// <summary>
    /// Write a frame to the device and wait for the notification that answers it.
    /// The notification is best-effort: the Controller 69 firmware acknowledges
    /// the GATT write but does not send an answer notification (verified live -
    /// the command still takes effect), so a missing notification returns an
    /// empty array instead of throwing.
    /// </summary>
    public async Task<byte[]> WriteAndAwaitNotification(byte[] frame, int timeoutMilliseconds = 5000)
    {
        if (!DeviceConnected) throw new InvalidOperationException($"[BLE {Mac}] Device not connected");

        _notificationTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _writeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Write WITH response: the write characteristic only advertises
        // write-with-response (props 0x0A); the answer arrives as notification
        await Send(MessageType.BluetoothGATTWriteRequest, new BluetoothGATTWriteRequest
        {
            Address = Address,
            Handle = WriteHandle,
            Response = true,
            Data = ByteString.CopyFrom(frame)
        });
        _logger.LogOutgoing($"[BLE {Mac}] Wrote {frame.Length} bytes: {Convert.ToHexString(frame)}");

        try
        {
            await _writeTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(3000));
            _logger.LogIncoming($"[BLE {Mac}] Write acknowledged");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning($"[BLE {Mac}] No write acknowledgement received");
        }

        try
        {
            var response = await _notificationTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
            return response;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning($"[BLE {Mac}] No answer notification - command was written, continuing");
            return Array.Empty<byte>();
        }
        finally
        {
            _notificationTcs = null;
        }
    }

    public async Task DisconnectAsync()
    {
        if (!DeviceConnected) return;

        _disconnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await Send(MessageType.BluetoothDeviceRequest, new BluetoothDeviceRequest
            {
                Address = Address,
                RequestType = BluetoothDeviceRequestType.Disconnect
            });
            _logger.LogOutgoing($"[BLE {Mac}] Disconnect request sent");

            // Wait for the connected=false confirmation so it cannot race a
            // subsequent connect attempt
            await _disconnectTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(3000));
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[BLE {Mac}] Disconnect failed: {ex.Message}");
        }
        finally
        {
            _disconnectTcs = null;
            DeviceConnected = false;
            WriteHandle = 0;
            NotifyHandle = 0;
        }
    }

    private async Task Send(MessageType type, IMessage message)
    {
        var connection = _proxy.Connection ??
                         throw new InvalidOperationException($"Proxy {_proxy.Host} is not connected");
        await connection.SendMessage((uint)type, message);
    }

    private void OnBluetoothMessage(MessageType msgType, byte[] msgData)
    {
        try
        {
            switch (msgType)
            {
                case MessageType.BluetoothLERawAdvertisementsResponse:
                {
                    var batch = BluetoothLERawAdvertisementsResponse.Parser.ParseFrom(msgData);
                    foreach (var adv in batch.Advertisements)
                    {
                        if (adv.Address != Address) continue;
                        var payload = AcInfinityProtocol.ExtractManufacturerData(adv.Data.Span);
                        if (payload != null)
                            AdvertisementReceived?.Invoke(payload);
                    }

                    break;
                }

                case MessageType.BluetoothDeviceConnectionResponse:
                {
                    var response = BluetoothDeviceConnectionResponse.Parser.ParseFrom(msgData);
                    if (response.Address != Address) break;

                    if (response.Connected)
                    {
                        DeviceConnected = true;
                        _logger.LogIncoming($"[BLE {Mac}] Connected (MTU {response.Mtu})");
                        _connectTcs?.TrySetResult(true);
                    }
                    else
                    {
                        DeviceConnected = false;
                        WriteHandle = 0;
                        NotifyHandle = 0;

                        // Expected disconnect: just confirm it
                        if (_disconnectTcs?.TrySetResult(true) == true) break;

                        var error = new InvalidOperationException(
                            $"[BLE {Mac}] Connection failed/closed (error {response.Error})");
                        _connectTcs?.TrySetException(error);
                        _writeTcs?.TrySetException(error);
                        _notificationTcs?.TrySetException(error);
                        _servicesDoneTcs?.TrySetException(error);
                    }

                    break;
                }

                case MessageType.BluetoothGATTGetServicesResponse:
                {
                    var response = BluetoothGATTGetServicesResponse.Parser.ParseFrom(msgData);
                    if (response.Address == Address)
                        _services.AddRange(response.Services);
                    break;
                }

                case MessageType.BluetoothGATTGetServicesDoneResponse:
                {
                    var response = BluetoothGATTGetServicesDoneResponse.Parser.ParseFrom(msgData);
                    if (response.Address == Address)
                        _servicesDoneTcs?.TrySetResult(true);
                    break;
                }

                case MessageType.BluetoothGATTNotifyDataResponse:
                {
                    var response = BluetoothGATTNotifyDataResponse.Parser.ParseFrom(msgData);
                    if (response.Address != Address) break;

                    var data = response.Data.ToByteArray();
                    _logger.LogIncoming($"[BLE {Mac}] Notification (handle {response.Handle}): {Convert.ToHexString(data)}");
                    _notificationTcs?.TrySetResult(data);
                    break;
                }

                case MessageType.BluetoothGATTWriteResponse:
                {
                    var response = BluetoothGATTWriteResponse.Parser.ParseFrom(msgData);
                    if (response.Address == Address)
                        _writeTcs?.TrySetResult(true);
                    break;
                }

                case MessageType.BluetoothGATTNotifyResponse:
                {
                    var response = BluetoothGATTNotifyResponse.Parser.ParseFrom(msgData);
                    if (response.Address == Address)
                        _notifyEnableTcs?.TrySetResult(true);
                    break;
                }

                case MessageType.BluetoothGATTErrorResponse:
                {
                    var response = BluetoothGATTErrorResponse.Parser.ParseFrom(msgData);
                    if (response.Address != Address) break;

                    var error = new InvalidOperationException(
                        $"[BLE {Mac}] GATT error {response.Error} (handle {response.Handle})");
                    _writeTcs?.TrySetException(error);
                    _notificationTcs?.TrySetException(error);
                    _servicesDoneTcs?.TrySetException(error);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[BLE {Mac}] Failed to handle {msgType}: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert the protobuf UUID representation (two uint64, big endian) or a
    /// short UUID into an uppercase hex string without dashes for comparison.
    /// </summary>
    private static string UuidToHex(IReadOnlyList<ulong> uuid, uint shortUuid)
    {
        if (shortUuid != 0)
            return $"0000{shortUuid:X4}00001000800000805F9B34FB";

        if (uuid.Count == 2)
            return $"{uuid[0]:X16}{uuid[1]:X16}";

        return "";
    }
}
