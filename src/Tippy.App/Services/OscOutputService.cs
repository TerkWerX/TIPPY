using System.Net.Sockets;
using System.Text;

namespace Tippy.App.Services;

public sealed class OscOutputService : IDisposable
{
    private readonly UdpClient _client = new();

    public void Send(string host, int port, string address, string? values)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith('/'))
            throw new ArgumentException("An OSC address must begin with /.");
        var arguments = string.IsNullOrWhiteSpace(values)
            ? []
            : values.Split(',', StringSplitOptions.TrimEntries);
        var tags = new StringBuilder(",");
        using var stream = new MemoryStream();
        WriteString(stream, address);
        foreach (var argument in arguments)
            tags.Append(int.TryParse(argument, out _) ? 'i' : float.TryParse(argument, out _) ? 'f' : 's');
        WriteString(stream, tags.ToString());
        foreach (var argument in arguments)
        {
            if (int.TryParse(argument, out var integer)) WriteInt32(stream, integer);
            else if (float.TryParse(argument, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var number)) WriteFloat(stream, number);
            else WriteString(stream, argument);
        }
        _client.Send(stream.ToArray(), new System.Net.IPEndPoint(
            System.Net.Dns.GetHostAddresses(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host)[0],
            Math.Clamp(port, 1, 65535)));
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes);
        stream.WriteByte(0);
        while (stream.Length % 4 != 0) stream.WriteByte(0);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteFloat(Stream stream, float value) =>
        WriteInt32(stream, BitConverter.SingleToInt32Bits(value));

    public void Dispose() => _client.Dispose();
}
