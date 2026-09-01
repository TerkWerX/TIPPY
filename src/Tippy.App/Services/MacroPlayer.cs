using System.Collections.Concurrent;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public sealed class MacroPlayer : IDisposable
{
    private readonly WindowsInputService _input;
    private readonly VirtualGamepadService _gamepad;
    private readonly ConcurrentDictionary<string, HeldAction> _held = new();
    private readonly CancellationTokenSource _stopping = new();

    public MacroPlayer(WindowsInputService input, VirtualGamepadService gamepad)
    {
        _input = input;
        _gamepad = gamepad;
    }

    public event EventHandler<string>? PlaybackError;

    public void Handle(string triggerId, MacroDefinition macro, bool isPressed)
    {
        if (macro.TriggerMode == MacroTriggerMode.WhileHeld)
        {
            HandleHeld(triggerId, macro, isPressed);
            return;
        }

        if (isPressed)
        {
            _ = Task.Run(() => PlayOnceAsync(macro.Clone(), _stopping.Token));
        }
    }

    private void HandleHeld(string triggerId, MacroDefinition macro, bool isPressed)
    {
        if (isPressed)
        {
            var keys = macro.Steps
                .Where(step => step.Type is MacroStepType.KeyChord or MacroStepType.KeyDown)
                .SelectMany(step => step.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var buttons = macro.Steps
                .Where(step => step.Type == MacroStepType.GamepadButton)
                .Select(step => step.Value ?? "A")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            try
            {
                _input.KeyDown(keys);
                foreach (var button in buttons)
                {
                    _gamepad.SetButton(button, true);
                }
                _held[triggerId] = new HeldAction(keys, buttons);
            }
            catch (Exception exception)
            {
                PlaybackError?.Invoke(this, exception.Message);
            }
        }
        else if (_held.TryRemove(triggerId, out var held))
        {
            try
            {
                _input.KeyUp(held.Keys);
                foreach (var button in held.GamepadButtons)
                {
                    _gamepad.SetButton(button, false);
                }
            }
            catch (Exception exception)
            {
                PlaybackError?.Invoke(this, exception.Message);
            }
        }
    }

    private async Task PlayOnceAsync(MacroDefinition macro, CancellationToken token)
    {
        try
        {
            foreach (var step in macro.Steps)
            {
                token.ThrowIfCancellationRequested();
                switch (step.Type)
                {
                    case MacroStepType.KeyChord:
                        await _input.KeyChordAsync(step.Keys, step.DurationMs, token).ConfigureAwait(false);
                        break;
                    case MacroStepType.KeyDown:
                        _input.KeyDown(step.Keys);
                        break;
                    case MacroStepType.KeyUp:
                        _input.KeyUp(step.Keys);
                        break;
                    case MacroStepType.Text:
                        _input.TypeText(step.Value ?? string.Empty);
                        break;
                    case MacroStepType.MouseButton:
                        _input.MouseClick(step.Value ?? "Left");
                        break;
                    case MacroStepType.MouseWheel:
                        _input.MouseWheel(step.Amount == 0 ? 120 : step.Amount);
                        break;
                    case MacroStepType.Delay:
                        await Task.Delay(Math.Clamp(step.DurationMs, 0, 60_000), token).ConfigureAwait(false);
                        break;
                    case MacroStepType.GamepadButton:
                        await _gamepad.PulseAsync(step.Value ?? "A", step.DurationMs, token).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PlaybackError?.Invoke(this, exception.Message);
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        foreach (var pair in _held.ToArray())
        {
            HandleHeld(pair.Key, new MacroDefinition { TriggerMode = MacroTriggerMode.WhileHeld }, false);
        }
        _stopping.Dispose();
    }

    private sealed record HeldAction(string[] Keys, string[] GamepadButtons);
}
