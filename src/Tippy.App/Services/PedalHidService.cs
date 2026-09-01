using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HidSharp;
using Tippy.App.Models;
using Tippy.Core.Input;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public sealed class PedalHidService : IDisposable
{
    private readonly IPedalReportDecoder[] _builtInDecoders = [new InfinityReportDecoder()];
    private readonly List<LearnedPedalDefinition> _learnedDefinitions = [];
    private readonly object _definitionGate = new();
    private readonly ConcurrentDictionary<string, DeviceReader> _readers = new();
    private readonly CancellationTokenSource _stopping = new();
    private int _scanning;
    private int _rescanRequested;

    public event EventHandler<PedalConnectionEventArgs>? ConnectionChanged;
    public event EventHandler<PedalStateEventArgs>? StateChanged;
    public event EventHandler<string>? Diagnostic;

    public IReadOnlyCollection<PedalDeviceInfo> ConnectedDevices =>
        _readers.Values.Select(reader => reader.Info).ToArray();

    public void Start()
    {
        DeviceList.Local.Changed += DeviceListChanged;
        _ = ScanAsync();
    }

    public void ConfigureLearnedDevices(IEnumerable<LearnedPedalDefinition> definitions)
    {
        lock (_definitionGate)
        {
            _learnedDefinitions.Clear();
            foreach (var definition in definitions)
            {
                definition.Normalize();
                _learnedDefinitions.Add(definition);
            }
        }
    }

    public void AddLearnedDevice(LearnedPedalDefinition definition)
    {
        definition.Normalize();
        lock (_definitionGate)
        {
            _learnedDefinitions.RemoveAll(item => item.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
            _learnedDefinitions.Add(definition);
        }
        _ = ScanAsync();
    }

    public async Task ScanAsync()
    {
        if (Interlocked.Exchange(ref _scanning, 1) != 0 || _stopping.IsCancellationRequested)
        {
            if (!_stopping.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _rescanRequested, 1);
            }
            return;
        }

        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in DeviceList.Local.GetHidDevices())
            {
                IPedalReportDecoder? decoder;
                int switchCount;
                try
                {
                    decoder = FindDecoder(device, out switchCount);
                }
                catch (Exception exception)
                {
                    Diagnostic?.Invoke(this, $"Skipped HID {device.VendorID:X4}:{device.ProductID:X4}: {exception.Message}");
                    continue;
                }
                if (decoder is null)
                {
                    continue;
                }

                var key = CreateDeviceKey(device);
                seen.Add(key);
                if (_readers.ContainsKey(key))
                {
                    continue;
                }

                var info = new PedalDeviceInfo(
                    key,
                    SafeGet(() => device.GetProductName(), "Infinity foot control"),
                    device.VendorID,
                    device.ProductID,
                    device.DevicePath,
                    decoder.Name,
                    switchCount);
                var reader = new DeviceReader(device, info, decoder, OnStateChanged, OnReaderStopped);
                if (!_readers.TryAdd(key, reader))
                {
                    reader.Dispose();
                    continue;
                }

                if (!reader.Start(_stopping.Token))
                {
                    _readers.TryRemove(key, out _);
                    reader.Dispose();
                    Diagnostic?.Invoke(this, $"Could not open {info.DisplayName} in shared HID mode.");
                    continue;
                }
                Diagnostic?.Invoke(this, $"Opened {info.DisplayName} ({info.VidPid}).");
                ConnectionChanged?.Invoke(this, new PedalConnectionEventArgs(info, true));
            }

            foreach (var pair in _readers.ToArray())
            {
                if (!seen.Contains(pair.Key) && _readers.TryRemove(pair.Key, out var reader))
                {
                    reader.Dispose();
                    ConnectionChanged?.Invoke(this, new PedalConnectionEventArgs(reader.Info, false));
                }
            }
        }
        catch (Exception exception)
        {
            Diagnostic?.Invoke(this, $"HID scan failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
            if (Interlocked.Exchange(ref _rescanRequested, 0) != 0 && !_stopping.IsCancellationRequested)
            {
                _ = ScanAsync();
            }
        }

        await Task.CompletedTask;
    }

    private void DeviceListChanged(object? sender, DeviceListChangedEventArgs e) => _ = ScanAsync();

    private void OnStateChanged(PedalDeviceInfo info, PedalTransition transition, byte[] report) =>
        StateChanged?.Invoke(this,
            new PedalStateEventArgs(info, transition.SwitchIndex, transition.IsPressed, report));

    private void OnReaderStopped(DeviceReader stopped, string? reason)
    {
        if (_readers.TryRemove(stopped.Info.DeviceKey, out _))
        {
            Diagnostic?.Invoke(this, $"{stopped.Info.DisplayName} reader stopped{(string.IsNullOrWhiteSpace(reason) ? "." : $": {reason}")}");
            ConnectionChanged?.Invoke(this, new PedalConnectionEventArgs(stopped.Info, false));
        }
    }

    private static string CreateDeviceKey(HidDevice device)
    {
        var serial = SafeGet(device.GetSerialNumber, string.Empty);
        if (!string.IsNullOrWhiteSpace(serial))
        {
            return $"{device.VendorID:X4}:{device.ProductID:X4}:{serial}";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(device.DevicePath.ToUpperInvariant()));
        return $"{device.VendorID:X4}:{device.ProductID:X4}:{Convert.ToHexString(hash.AsSpan(0, 6))}";
    }

    private IPedalReportDecoder? FindDecoder(HidDevice device, out int switchCount)
    {
        switchCount = 3;
        var builtIn = _builtInDecoders.FirstOrDefault(candidate =>
            candidate.Supports(device.VendorID, device.ProductID));
        if (builtIn is not null)
        {
            return builtIn;
        }

        LearnedPedalDefinition[] possibleDefinitions;
        lock (_definitionGate)
        {
            possibleDefinitions = _learnedDefinitions.Where(item =>
                item.VendorId == device.VendorID && item.ProductId == device.ProductID).ToArray();
        }
        if (possibleDefinitions.Length == 0)
        {
            return null;
        }

        var productName = SafeGet(device.GetProductName, string.Empty);
        var descriptorHash = Convert.ToHexString(SHA256.HashData(device.GetRawReportDescriptor()));
        var reportLength = device.GetMaxInputReportLength();
        var definition = possibleDefinitions.FirstOrDefault(item =>
                item.ReportLength == reportLength &&
                (string.IsNullOrWhiteSpace(item.ProductName) ||
                 item.ProductName.Equals(productName, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(item.ReportDescriptorHash) ||
                 item.ReportDescriptorHash.Equals(descriptorHash, StringComparison.OrdinalIgnoreCase)));
        if (definition is null)
        {
            return null;
        }
        switchCount = Math.Max(1, definition.Switches.Count);
        return new LearnedReportDecoder(definition);
    }

    private static string SafeGet(Func<string> getter, string fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    public void Dispose()
    {
        DeviceList.Local.Changed -= DeviceListChanged;
        _stopping.Cancel();
        foreach (var reader in _readers.Values)
        {
            reader.Dispose();
        }
        _readers.Clear();
        _stopping.Dispose();
    }

    private sealed class DeviceReader : IDisposable
    {
        private readonly HidDevice _device;
        private readonly IPedalReportDecoder _decoder;
        private readonly Action<PedalDeviceInfo, PedalTransition, byte[]> _stateCallback;
        private readonly Action<DeviceReader, string?> _stoppedCallback;
        private readonly bool[] _state = new bool[3];
        private HidStream? _stream;
        private CancellationTokenSource? _linkedCancellation;
        private int _disposed;

        public DeviceReader(
            HidDevice device,
            PedalDeviceInfo info,
            IPedalReportDecoder decoder,
            Action<PedalDeviceInfo, PedalTransition, byte[]> stateCallback,
            Action<DeviceReader, string?> stoppedCallback)
        {
            _device = device;
            Info = info;
            _decoder = decoder;
            _stateCallback = stateCallback;
            _stoppedCallback = stoppedCallback;
        }

        public PedalDeviceInfo Info { get; }

        public bool Start(CancellationToken stopping)
        {
            if (!_device.TryOpen(out var stream))
            {
                return false;
            }

            _stream = stream;
            _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            _ = ReadLoopAsync(_linkedCancellation.Token);
            return true;
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            string? stopReason = null;
            try
            {
                var length = Math.Max(8, _device.GetMaxInputReportLength());
                var buffer = new byte[length];
                while (!cancellationToken.IsCancellationRequested && _stream is not null)
                {
                    int read;
                    try
                    {
                        read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
                    {
                        continue;
                    }
                    if (read <= 0)
                    {
                        break;
                    }

                    var report = buffer.AsSpan(0, read).ToArray();
                    foreach (var transition in _decoder.Decode(report, _state))
                    {
                        _stateCallback(Info, transition, report);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                stopReason = $"{exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    _stoppedCallback(this, stopReason);
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            _linkedCancellation?.Cancel();
            _stream?.Dispose();
            _linkedCancellation?.Dispose();
        }
    }
}
