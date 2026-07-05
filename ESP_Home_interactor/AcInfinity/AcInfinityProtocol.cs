using System.Text;

namespace ESP_Home_Interactor.AcInfinity;

/// <summary>
/// AC Infinity BLE application protocol.
/// Ported from hunterjm/ac-infinity-ble (protocol.py / util.py).
///
/// Frame layout (AddHead):
///   [0]  0xA5  [1] 0x00                      head
///   [2..3]  payload length (big endian)
///   [4..5]  sequence number (big endian)
///   [6..7]  CRC16 over bytes 0-5
///   [8]  0x00  [9] command type
///   [10..]  payload
///   [end-1..end]  CRC16 over bytes 8..(9+payload length)
/// </summary>
public static class AcInfinityProtocol
{
    public const int ManufacturerId = 2306;

    /// <summary>Controller types that address multiple ports (Controller 69 family)</summary>
    private static readonly int[] MultiPortTypes = { 7, 9, 11, 12 };

    /// <summary>
    /// Build a set-level command. workType: 1 = off, 2 = on. Level 0-10.
    /// </summary>
    public static byte[] BuildSetLevel(int controllerType, int workType, int level, int port, int sequence)
    {
        if (workType is not (1 or 2))
            throw new ArgumentException("Work type must be 1 (off) or 2 (on)", nameof(workType));
        if (level is < 0 or > 10)
            throw new ArgumentException("Level must be between 0 and 10", nameof(level));

        var payload = new List<byte> { 16, 1, (byte)workType, (byte)(workType + 16), 1, (byte)level };
        if (MultiPortTypes.Contains(controllerType))
        {
            payload.Add(255);
            payload.Add((byte)port);
        }

        return AddHead(payload.ToArray(), 3, sequence);
    }

    /// <summary>
    /// Build a get-model-data command (state query). Response carries work type and levels.
    /// </summary>
    public static byte[] BuildGetModelData(int controllerType, int port, int sequence)
    {
        var payload = new List<byte> { 16, 17, 18, 19, 20, 21, 22, 23 };
        if (MultiPortTypes.Contains(controllerType))
        {
            payload.Add(255);
            payload.Add((byte)port);
        }

        return AddHead(payload.ToArray(), 1, sequence);
    }

    private static byte[] AddHead(byte[] payload, byte commandType, int sequence)
    {
        var result = new byte[payload.Length + 12];
        result[0] = 0xA5;
        result[1] = 0x00;
        result[2] = (byte)((payload.Length >> 8) & 0xFF);
        result[3] = (byte)(payload.Length & 0xFF);
        result[4] = (byte)((sequence >> 8) & 0xFF);
        result[5] = (byte)(sequence & 0xFF);

        var headCrc = Crc16(result, 0, 6);
        result[6] = headCrc[0];
        result[7] = headCrc[1];

        result[8] = 0;
        result[9] = commandType;
        Array.Copy(payload, 0, result, 10, payload.Length);

        var tailCrc = Crc16(result, 8, payload.Length + 2);
        result[^2] = tailCrc[0];
        result[^1] = tailCrc[1];

        return result;
    }

    /// <summary>
    /// CRC16 variant used by AC Infinity (init 0xFFFF, byte-swap fold).
    /// Returns [high byte, low byte].
    /// </summary>
    public static byte[] Crc16(byte[] data, int offset, int count)
    {
        var b = 0xFFFF;
        for (var i = offset; i < offset + count; i++)
        {
            var b2 = (((b << 8) | (b >> 8)) & 0xFFFF) ^ data[i];
            var b3 = b2 ^ ((b2 & 0xFF) >> 4);
            var b4 = b3 ^ ((b3 << 12) & 0xFFFF);
            b = b4 ^ (((b4 & 0xFF) << 5) & 0xFFFF);
        }

        return new[] { (byte)((b >> 8) & 0xFF), (byte)(b & 0xFF) };
    }

    /// <summary>
    /// Parse the manufacturer-specific payload (company id 2306 already stripped)
    /// from a BLE advertisement into controller state.
    /// </summary>
    public static AcInfinityAdvertisement? ParseManufacturerData(byte[] data)
    {
        if (data.Length < 19) return null;

        var type = data[12];
        var adv = new AcInfinityAdvertisement
        {
            Type = type,
            Version = data[11],
            Name = $"{GetTypePrefix(type)}-{Encoding.ASCII.GetString(data, 6, 5)}",
            FanState = GetBits(data[13], 2, 2),
            Temperature = GetShort(data, 14) / 100.0,
            Humidity = GetShort(data, 16) / 100.0,
            Fan = data[18]
        };

        if (adv.Version >= 3 && MultiPortTypes.Contains(type) && data.Length >= 23)
        {
            adv.ChoosePort = data[19];
            adv.Vpd = GetShort(data, 21) / 100.0;
        }

        return adv;
    }

    /// <summary>
    /// Extract the AC Infinity manufacturer payload from a raw BLE advertisement
    /// (sequence of AD structures). Returns null when the advertisement is not
    /// from an AC Infinity device.
    /// </summary>
    public static byte[]? ExtractManufacturerData(ReadOnlySpan<byte> rawAdvertisement)
    {
        var offset = 0;
        while (offset < rawAdvertisement.Length)
        {
            var recordLength = rawAdvertisement[offset];
            if (recordLength == 0) break;
            if (offset + 1 + recordLength > rawAdvertisement.Length) break;

            var adType = rawAdvertisement[offset + 1];
            if (adType == 0xFF && recordLength >= 3)
            {
                // Company ID is little endian
                var companyId = rawAdvertisement[offset + 2] | (rawAdvertisement[offset + 3] << 8);
                if (companyId == ManufacturerId)
                    return rawAdvertisement.Slice(offset + 4, recordLength - 3).ToArray();
            }

            offset += recordLength + 1;
        }

        return null;
    }

    private static string GetTypePrefix(int type) => type switch
    {
        2 => "B",
        3 or 4 or 5 or 14 or 15 => "C",
        6 => "D",
        7 or 8 => "E",
        9 or 12 => "F",
        11 => "G",
        _ => "A"
    };

    private static short GetShort(byte[] data, int i) => (short)((data[i] << 8) | data[i + 1]);

    private static int GetBits(byte b, int i, int count) => (b >> (8 - i - count)) & (0xFF >> (8 - count));
}

/// <summary>State broadcast by the controller in its BLE advertisements</summary>
public class AcInfinityAdvertisement
{
    public required int Type { get; init; }
    public required int Version { get; init; }
    public required string Name { get; init; }
    public int FanState { get; init; }
    public double Temperature { get; init; }
    public double Humidity { get; init; }
    public int Fan { get; init; }
    public int? ChoosePort { get; set; }
    public double? Vpd { get; set; }
}
