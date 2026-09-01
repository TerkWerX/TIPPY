namespace Tippy.App.Services;

public sealed record ObservedPedalPress(string DeviceKey, string DeviceName, int SwitchIndex);

public sealed class PedalActivityHub
{
    public event EventHandler<ObservedPedalPress>? Pressed;
    public void Publish(string deviceKey, string deviceName, int switchIndex) =>
        Pressed?.Invoke(this, new ObservedPedalPress(deviceKey, deviceName, switchIndex));
}
