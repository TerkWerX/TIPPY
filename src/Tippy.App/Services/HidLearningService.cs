using System.Security.Cryptography;
using System.IO;
using HidSharp;
using Tippy.App.Models;

namespace Tippy.App.Services;

public sealed class HidLearningService
{
    public IReadOnlyList<HidCandidateInfo> ListCandidates()
    {
        List<HidCandidateInfo> candidates = [];
        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            try
            {
                var reportLength = device.GetMaxInputReportLength();
                if (reportLength <= 0)
                {
                    continue;
                }
                var product = SafeGet(device.GetProductName, "Unnamed HID device");
                var manufacturer = SafeGet(device.GetManufacturer, string.Empty);
                var descriptor = device.GetRawReportDescriptor();
                var looksLikePedal = product.Contains("pedal", StringComparison.OrdinalIgnoreCase) ||
                                     product.Contains("foot", StringComparison.OrdinalIgnoreCase) ||
                                     product.Contains("switch", StringComparison.OrdinalIgnoreCase) ||
                                     ContainsSequence(descriptor, [0x05, 0x09]);
                candidates.Add(new HidCandidateInfo(
                    device.DevicePath,
                    product,
                    manufacturer,
                    device.VendorID,
                    device.ProductID,
                    reportLength,
                    Convert.ToHexString(SHA256.HashData(descriptor)),
                    looksLikePedal));
            }
            catch
            {
            }
        }
        return candidates
            .OrderByDescending(candidate => candidate.LooksLikePedal)
            .ThenBy(candidate => candidate.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<(byte[] Pressed, byte[] Released)> CapturePressReleaseAsync(
        HidCandidateInfo candidate,
        IProgress<byte[]>? pressedProgress,
        CancellationToken cancellationToken)
    {
        var device = DeviceList.Local.GetHidDevices(candidate.VendorId, candidate.ProductId)
            .FirstOrDefault(item => item.DevicePath.Equals(candidate.DevicePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new IOException("The selected HID device is no longer connected.");
        if (!device.TryOpen(out var stream))
        {
            throw new IOException("Windows could not open the selected HID device. Close its vendor software and try again.");
        }

        using (stream)
        {
            var pressed = await ReadNextAsync(stream, candidate.ReportLength, null, cancellationToken).ConfigureAwait(false);
            pressedProgress?.Report(pressed);
            var released = await ReadNextAsync(stream, candidate.ReportLength, pressed, cancellationToken).ConfigureAwait(false);
            return (pressed, released);
        }
    }

    private static async Task<byte[]> ReadNextAsync(
        HidStream stream,
        int reportLength,
        byte[]? differentFrom,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[reportLength];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new IOException("The HID device stopped sending data.");
                }
                var report = buffer.AsSpan(0, read).ToArray();
                if (differentFrom is null || !report.AsSpan().SequenceEqual(differentFrom))
                {
                    return report;
                }
            }
            catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (TimeoutException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    private static string SafeGet(Func<string> getter, string fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }

    private static bool ContainsSequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> sequence)
    {
        if (sequence.IsEmpty || sequence.Length > source.Length) return false;
        for (var index = 0; index <= source.Length - sequence.Length; index++)
        {
            if (source.Slice(index, sequence.Length).SequenceEqual(sequence)) return true;
        }
        return false;
    }
}
