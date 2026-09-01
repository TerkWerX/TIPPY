namespace Tippy.App.Models;

public sealed record PedalDeviceInfo(
    string DeviceKey,
    string DisplayName,
    int VendorId,
    int ProductId,
    string DevicePath,
    string DecoderName,
    int SwitchCount = 3,
    string Manufacturer = "")
{
    public string VidPid => $"VID_{VendorId:X4}  PID_{ProductId:X4}";
}

public sealed class PedalConnectionEventArgs(PedalDeviceInfo device, bool isConnected) : EventArgs
{
    public PedalDeviceInfo Device { get; } = device;
    public bool IsConnected { get; } = isConnected;
}

public sealed class PedalStateEventArgs(
    PedalDeviceInfo device,
    int switchIndex,
    bool isPressed,
    byte[] rawReport,
    bool isSynthetic = false) : EventArgs
{
    public PedalDeviceInfo Device { get; } = device;
    public int SwitchIndex { get; } = switchIndex;
    public bool IsPressed { get; } = isPressed;
    public byte[] RawReport { get; } = rawReport;
    public bool IsSynthetic { get; } = isSynthetic;
}
