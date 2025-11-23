using System.Net.Sockets;
using Google.Protobuf;

namespace ESP_Home_Interactor;

/// <summary>
/// Low-level ESPHome protocol connection handler
/// Manages TCP socket communication and protocol message framing
/// </summary>
public class ESPHomeConnection : IDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;

    public ESPHomeConnection(Socket socket, NetworkStream stream)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public NetworkStream Stream => _stream;
    public bool DataAvailable => _stream.DataAvailable;

    /// <summary>
    /// Send a protobuf message with the ESPHome protocol framing
    /// Format: [0x00 preamble][VarInt length][VarInt type][protobuf data]
    /// </summary>
    public async Task SendMessage(uint messageType, IMessage message)
    {
        var data = message.ToByteArray();

        // Write preamble (0x00)
        await _stream.WriteAsync(new byte[] { 0x00 });

        // Write message length as VarInt
        await WriteVarInt((uint)data.Length);

        // Write message type as VarInt
        await WriteVarInt(messageType);

        // Write message data
        await _stream.WriteAsync(data);
        await _stream.FlushAsync();
    }

    /// <summary>
    /// Read a protobuf message from the stream
    /// Returns the message type and raw protobuf data
    /// </summary>
    public async Task<(uint type, byte[] data)> ReadMessage()
    {
        // Read preamble (0x00)
        var preamble = new byte[1];
        var bytesRead = await _stream.ReadAsync(preamble);
        if (bytesRead == 0)
            throw new EndOfStreamException("Connection closed by remote host");

        if (preamble[0] != 0x00)
        {
            // Log stream position context for debugging
            var nextBytes = new byte[16];
            var peekRead = await _stream.ReadAsync(nextBytes);
            var hexDump = peekRead > 0
                ? BitConverter.ToString(nextBytes, 0, peekRead).Replace("-", " ")
                : "none";
            throw new InvalidDataException(
                $"Invalid preamble: expected 0x00, got 0x{preamble[0]:X2}. " +
                $"Next {peekRead} bytes: {hexDump}");
        }

        // Read message length as VarInt
        var length = await ReadVarInt();

        // Read message type as VarInt
        var type = await ReadVarInt();

        // Read message data
        var data = new byte[length];
        if (length > 0)
            await _stream.ReadExactlyAsync(data);

        return (type, data);
    }

    /// <summary>
    /// Write an unsigned integer as a VarInt (variable-length integer)
    /// </summary>
    private async Task WriteVarInt(uint value)
    {
        while (value >= 0x80)
        {
            await _stream.WriteAsync(new byte[] { (byte)((value & 0x7F) | 0x80) });
            value >>= 7;
        }
        await _stream.WriteAsync(new byte[] { (byte)value });
    }

    /// <summary>
    /// Read a VarInt (variable-length integer) from the stream
    /// </summary>
    private async Task<uint> ReadVarInt()
    {
        uint result = 0;
        int shift = 0;

        while (true)
        {
            var buffer = new byte[1];
            await _stream.ReadExactlyAsync(buffer);
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

    /// <summary>
    /// Close the connection gracefully
    /// </summary>
    public void Close()
    {
        _socket.Close();
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _socket?.Dispose();
    }
}
