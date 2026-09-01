using System.Diagnostics;
using HidSharp;

const int vendorId = 0x05F3;
const int productId = 0x00FF;
var seconds = args.Length > 0 && int.TryParse(args[0], out var requested) ? requested : 20;
var devices = DeviceList.Local.GetHidDevices(vendorId, productId).ToArray();

Console.WriteLine($"Found {devices.Length} Infinity-family device(s).");
foreach (var device in devices)
{
    Console.WriteLine($"{device.GetProductName()} | {device.DevicePath} | input {device.GetMaxInputReportLength()} bytes | feature {device.GetMaxFeatureReportLength()} bytes");
    Console.WriteLine($"Descriptor: {BitConverter.ToString(device.GetRawReportDescriptor())}");
}

if (args.Contains("--async", StringComparer.OrdinalIgnoreCase))
{
    await Task.WhenAll(devices.Select(device => ReadAsync(device, seconds)));
}
else
{
    await Task.WhenAll(devices.Select(device => Task.Run(() => Read(device, seconds))));
}

static async Task ReadAsync(HidDevice device, int seconds)
{
    if (!device.TryOpen(out var stream))
    {
        Console.WriteLine($"ASYNC OPEN FAILED | {device.DevicePath}");
        return;
    }
    using (stream)
    using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
    {
        var buffer = new byte[Math.Max(3, device.GetMaxInputReportLength())];
        try
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellation.Token);
            Console.WriteLine($"ASYNC RETURN {device.GetProductName()} | {read} | {BitConverter.ToString(buffer, 0, read)}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"ASYNC {device.GetProductName()} | {exception.GetType().Name} | {exception.Message}");
        }
    }
}

static void Read(HidDevice device, int seconds)
{
    if (!device.TryOpen(out var stream))
    {
        Console.WriteLine($"OPEN FAILED | {device.DevicePath}");
        return;
    }

    using (stream)
    {
        stream.ReadTimeout = 250;
        var buffer = new byte[Math.Max(3, device.GetMaxInputReportLength())];
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            try
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    Console.WriteLine($"{device.GetProductName(),-24} {watch.ElapsedMilliseconds,6} ms | {BitConverter.ToString(buffer, 0, read)}");
                }
            }
            catch (TimeoutException)
            {
            }
        }
    }
}
