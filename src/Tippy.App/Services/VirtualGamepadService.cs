using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Tippy.App.Services;

public sealed class VirtualGamepadService : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private readonly object _gate = new();

    public bool IsAvailable { get; private set; }
    public string Status { get; private set; } = "Virtual controller not started";

    public bool TryInitialize()
    {
        lock (_gate)
        {
            if (_controller is not null)
            {
                return true;
            }
            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateXbox360Controller();
                _controller.Connect();
                IsAvailable = true;
                Status = "Virtual Xbox 360 controller connected";
                return true;
            }
            catch (Exception exception)
            {
                _controller?.Disconnect();
                _controller = null;
                _client?.Dispose();
                _client = null;
                IsAvailable = false;
                Status = $"Gamepad driver unavailable: {exception.Message}";
                return false;
            }
        }
    }

    public void SetButton(string button, bool pressed)
    {
        if (!TryInitialize())
        {
            throw new InvalidOperationException(Status);
        }
        _controller!.SetButtonState(ParseButton(button), pressed);
    }

    public async Task PulseAsync(string button, int durationMs, CancellationToken token)
    {
        SetButton(button, true);
        try
        {
            await Task.Delay(Math.Clamp(durationMs, 1, 5_000), token).ConfigureAwait(false);
        }
        finally
        {
            SetButton(button, false);
        }
    }

    public void SetAxis(string axis, int percentage)
    {
        if (!TryInitialize()) throw new InvalidOperationException(Status);
        var normalized = NormalizeAxisName(axis);
        var value = Math.Clamp(percentage, normalized.Contains("Trigger", StringComparison.OrdinalIgnoreCase) ? 0 : -100, 100);
        switch (normalized)
        {
            case "Left X":
                _controller!.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxisValue(value));
                break;
            case "Left Y":
                _controller!.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxisValue(value));
                break;
            case "Right X":
                _controller!.SetAxisValue(Xbox360Axis.RightThumbX, ToAxisValue(value));
                break;
            case "Right Y":
                _controller!.SetAxisValue(Xbox360Axis.RightThumbY, ToAxisValue(value));
                break;
            case "Left Trigger":
                _controller!.SetSliderValue(Xbox360Slider.LeftTrigger, ToSliderValue(value));
                break;
            case "Right Trigger":
                _controller!.SetSliderValue(Xbox360Slider.RightTrigger, ToSliderValue(value));
                break;
        }
    }

    public async Task PulseAxisAsync(string axis, int percentage, int durationMs, CancellationToken token)
    {
        SetAxis(axis, percentage);
        try { await Task.Delay(Math.Clamp(durationMs, 1, 5_000), token).ConfigureAwait(false); }
        finally { SetAxis(axis, 0); }
    }

    public static string NormalizeAxisName(string value) => value.Trim().ToUpperInvariant() switch
    {
        "LEFT X" or "LX" or "LEFT STICK X" => "Left X",
        "LEFT Y" or "LY" or "LEFT STICK Y" => "Left Y",
        "RIGHT X" or "RX" or "RIGHT STICK X" => "Right X",
        "RIGHT Y" or "RY" or "RIGHT STICK Y" => "Right Y",
        "LEFT TRIGGER" or "LT" => "Left Trigger",
        "RIGHT TRIGGER" or "RT" => "Right Trigger",
        _ => throw new ArgumentException($"Unknown Xbox axis or trigger: {value}")
    };

    public static short ToAxisValue(int percentage)
    {
        var value = Math.Clamp(percentage, -100, 100);
        return value < 0
            ? (short)Math.Round(value * (short.MinValue / -100d))
            : (short)Math.Round(value * (short.MaxValue / 100d));
    }

    public static byte ToSliderValue(int percentage) =>
        (byte)Math.Round(Math.Clamp(percentage, 0, 100) * (byte.MaxValue / 100d));

    private static Xbox360Button ParseButton(string value) => value.Trim().ToUpperInvariant() switch
    {
        "B" => Xbox360Button.B,
        "X" => Xbox360Button.X,
        "Y" => Xbox360Button.Y,
        "BACK" => Xbox360Button.Back,
        "START" => Xbox360Button.Start,
        "GUIDE" => Xbox360Button.Guide,
        "LB" or "LEFT SHOULDER" => Xbox360Button.LeftShoulder,
        "RB" or "RIGHT SHOULDER" => Xbox360Button.RightShoulder,
        "L3" or "LEFT THUMB" => Xbox360Button.LeftThumb,
        "R3" or "RIGHT THUMB" => Xbox360Button.RightThumb,
        "DPAD UP" => Xbox360Button.Up,
        "DPAD DOWN" => Xbox360Button.Down,
        "DPAD LEFT" => Xbox360Button.Left,
        "DPAD RIGHT" => Xbox360Button.Right,
        _ => Xbox360Button.A
    };

    public void Dispose()
    {
        lock (_gate)
        {
            _controller?.Disconnect();
            _controller = null;
            _client?.Dispose();
            _client = null;
        }
    }
}
