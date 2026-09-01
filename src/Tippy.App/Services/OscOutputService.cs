using System.Net.Sockets;
using System.Text;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public sealed class OscOutputService : IDisposable
{
    private readonly UdpClient _client = new();
    private OscOutputSettings _settings = new();

    public void Configure(OscOutputSettings settings)
    {
        settings.Normalize();
        _settings = settings.Clone();
    }

    public void Send(MacroStep step)
    {
        var preset = string.IsNullOrWhiteSpace(step.EndpointPresetId) ? null : _settings.Resolve(step.EndpointPresetId);
        Send(preset?.Host ?? step.WorkingDirectory ?? "127.0.0.1",
            preset?.Port ?? (step.Amount == 0 ? 8000 : step.Amount),
            step.Value ?? "/tippy", step.Arguments);
    }

    public void Send(string host, int port, string address, string? values)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith('/'))
            throw new ArgumentException("An OSC address must begin with /.");
        var bytes = BuildPacket(address, values);
        var addresses = System.Net.Dns.GetHostAddresses(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host);
        var destination = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
                          ?? addresses.FirstOrDefault()
                          ?? throw new InvalidOperationException($"OSC host could not be resolved: {host}");
        _client.Send(bytes, new System.Net.IPEndPoint(destination, Math.Clamp(port, 1, 65535)));
    }

    public static byte[] BuildPacket(string address, string? values)
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
            tags.Append(int.TryParse(argument, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out _) ? 'i' :
                float.TryParse(argument, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _) ? 'f' : 's');
        WriteString(stream, tags.ToString());
        foreach (var argument in arguments)
        {
            if (int.TryParse(argument, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var integer)) WriteInt32(stream, integer);
            else if (float.TryParse(argument, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var number)) WriteFloat(stream, number);
            else WriteString(stream, argument);
        }
        return stream.ToArray();
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
