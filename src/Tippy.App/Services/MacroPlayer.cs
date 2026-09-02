using System.Diagnostics;
using Tippy.Core.Input;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public sealed record MacroOutputEventArgs(string TriggerId, string OutputKind, long DispatchedTimestamp);

public sealed class MacroPlayer : IDisposable
{
    private readonly WindowsInputService _input;
    private readonly VirtualGamepadService _gamepad;
    private readonly MidiOutputService _midi = new();
    private readonly OscOutputService _osc = new();
    private readonly HeldOutputLedger _outputs = new();
    private readonly GamepadAnalogLedger _analogOutputs = new();
    private readonly HashSet<string> _activeHeldOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _heldMouseOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _heldMouseCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _continuousOwners = new(StringComparer.Ordinal);
    private readonly object _outputGate = new();
    private readonly object _playbackGate = new();
    private CancellationTokenSource _playbackCancellation = new();
    private volatile bool _acceptingInput = true;
    private bool _disposed;
    private MacroSafetySettings _safety = new();

    public MacroPlayer(WindowsInputService input, VirtualGamepadService gamepad)
    {
        _input = input;
        _gamepad = gamepad;
    }

    public event EventHandler<string>? PlaybackError;
    public event EventHandler<MacroOutputEventArgs>? OutputDispatched;

    public void ConfigureSafety(MacroSafetySettings settings)
    {
        settings.Normalize();
        lock (_playbackGate) _safety = new MacroSafetySettings
        {
            MaximumMacroSeconds = settings.MaximumMacroSeconds,
            MaximumRepeatSeconds = settings.MaximumRepeatSeconds,
            MaximumSteps = settings.MaximumSteps,
            EmergencyStopHotkey = settings.EmergencyStopHotkey
        };
    }

    public void ConfigureMidi(MidiOutputSettings settings) => _midi.Configure(settings);
    public void ConfigureOsc(OscOutputSettings settings) => _osc.Configure(settings);

    public void Handle(string triggerId, MacroDefinition macro, bool isPressed)
    {
        try
        {
            ValidateKeys(macro);
        }
        catch (Exception exception)
        {
            PlaybackError?.Invoke(this, exception.Message);
            return;
        }
        CancellationToken playbackToken;
        var handleHeld = false;
        MacroDefinition? oneShot = null;
        lock (_playbackGate)
        {
            if (_disposed || !_acceptingInput) return;
            playbackToken = _playbackCancellation.Token;
            if (macro.TriggerMode == MacroTriggerMode.WhileHeld)
            {
                handleHeld = true;
            }
            else if ((macro.TriggerMode == MacroTriggerMode.PressOnce && isPressed) ||
                     (macro.TriggerMode == MacroTriggerMode.ReleaseOnce && !isPressed))
            {
                oneShot = macro.Clone();
            }
        }

        if (handleHeld)
        {
            HandleHeld(triggerId, macro, isPressed, playbackToken);
        }
        else if (oneShot is not null)
        {
            _ = PlayOnceAsync(triggerId, oneShot, playbackToken);
        }
    }

    public void ReleaseHeld(string triggerId) => ReleaseOwner(HeldOwnerId(triggerId));

    public void ReleaseAll()
    {
        CancellationTokenSource previous;
        lock (_playbackGate)
        {
            if (_disposed) return;
            previous = _playbackCancellation;
            _playbackCancellation = new CancellationTokenSource();
        }
        previous.Cancel();
        previous.Dispose();
        lock (_outputGate)
        {
            _activeHeldOwners.Clear();
            ReleaseAllMouseAndContinuous();
            TryApplyDelta(_outputs.ReleaseAll());
            TryApplyAnalog(_analogOutputs.ReleaseAll());
        }
    }

    public void Suspend()
    {
        CancellationTokenSource previous;
        lock (_playbackGate)
        {
            if (_disposed) return;
            _acceptingInput = false;
            previous = _playbackCancellation;
            _playbackCancellation = new CancellationTokenSource();
        }
        previous.Cancel();
        previous.Dispose();
        lock (_outputGate)
        {
            _activeHeldOwners.Clear();
            ReleaseAllMouseAndContinuous();
            TryApplyDelta(_outputs.ReleaseAll());
            TryApplyAnalog(_analogOutputs.ReleaseAll());
        }
    }

    public void Resume()
    {
        lock (_playbackGate)
        {
            if (!_disposed) _acceptingInput = true;
        }
    }

    private void HandleHeld(string triggerId, MacroDefinition macro, bool isPressed, CancellationToken token)
    {
        var ownerId = HeldOwnerId(triggerId);
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
            var axes = macro.Steps.Where(step => step.Type == MacroStepType.GamepadAxis).ToArray();
            var mouseButtons = macro.Steps.Where(step => step.Type == MacroStepType.MouseButton)
                .Select(step => step.Value ?? "Left").ToArray();
            try
            {
                lock (_outputGate)
                {
                    if (!_activeHeldOwners.Add(ownerId))
                    {
                        return;
                    }
                    ThrowIfInputPaused(token);
                    ApplyDelta(_outputs.Acquire(ownerId, keys, buttons));
                    foreach (var step in axes)
                        ApplyAnalog(_analogOutputs.Acquire(ownerId, step.Value ?? "Left X", step.Amount));
                    AcquireMouseButtons(ownerId, mouseButtons);
                }
                StartContinuous(ownerId, macro, token, triggerId);
                if (keys.Length > 0 || buttons.Length > 0 || axes.Length > 0 || mouseButtons.Length > 0)
                    RaiseOutput(triggerId, "held action");
            }
            catch (OperationCanceledException)
            {
                lock (_outputGate)
                {
                    _activeHeldOwners.Remove(ownerId);
                    ReleaseMouseAndContinuous(ownerId);
                    TryApplyDelta(_outputs.ReleaseOwner(ownerId));
                    TryApplyAnalog(_analogOutputs.ReleaseOwner(ownerId));
                }
            }
            catch (Exception exception)
            {
                lock (_outputGate)
                {
                    _activeHeldOwners.Remove(ownerId);
                    ReleaseMouseAndContinuous(ownerId);
                    TryApplyDelta(_outputs.ReleaseOwner(ownerId));
                    TryApplyAnalog(_analogOutputs.ReleaseOwner(ownerId));
                }
                PlaybackError?.Invoke(this, exception.Message);
            }
        }
        else
        {
            ReleaseOwner(ownerId);
        }
    }

    private async Task PlayOnceAsync(string triggerId, MacroDefinition macro, CancellationToken token)
    {
        var ownerId = $"play:{Guid.NewGuid():N}";
        using var safetyCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        safetyCancellation.CancelAfter(TimeSpan.FromSeconds(_safety.MaximumMacroSeconds));
        token = safetyCancellation.Token;
        try
        {
            if (macro.Steps.Count > _safety.MaximumSteps)
                throw new InvalidOperationException($"Macro has {macro.Steps.Count} steps; the safety limit is {_safety.MaximumSteps}.");
            foreach (var step in macro.Steps)
            {
                token.ThrowIfCancellationRequested();
                switch (step.Type)
                {
                    case MacroStepType.KeyChord:
                        Acquire(ownerId, step.Keys, [], token);
                        RaiseOutput(triggerId, "keyboard");
                        try
                        {
                            await Task.Delay(Math.Clamp(step.DurationMs, 1, 5_000), token).ConfigureAwait(false);
                        }
                        finally
                        {
                            Release(ownerId, step.Keys, []);
                        }
                        break;
                    case MacroStepType.KeyDown:
                        Acquire(ownerId, step.Keys, [], token);
                        RaiseOutput(triggerId, "keyboard");
                        break;
                    case MacroStepType.KeyUp:
                        Release(ownerId, step.Keys, []);
                        RaiseOutput(triggerId, "keyboard");
                        break;
                    case MacroStepType.Text:
                        ExecuteInjection(token, () => _input.TypeText(step.Value ?? string.Empty));
                        RaiseOutput(triggerId, "text");
                        break;
                    case MacroStepType.MouseButton:
                        ExecuteInjection(token, () => _input.MouseClick(step.Value ?? "Left"));
                        RaiseOutput(triggerId, "mouse");
                        break;
                    case MacroStepType.MouseWheel:
                        ExecuteInjection(token, () => ScrollMouse(step));
                        RaiseOutput(triggerId, "mouse");
                        break;
                    case MacroStepType.MouseMove:
                        ExecuteInjection(token, () => MoveMouse(step));
                        RaiseOutput(triggerId, "mouse");
                        break;
                    case MacroStepType.Delay:
                        await Task.Delay(Math.Clamp(step.DurationMs, 0, 60_000), token).ConfigureAwait(false);
                        break;
                    case MacroStepType.GamepadButton:
                        var button = step.Value ?? "A";
                        Acquire(ownerId, [], [button], token);
                        RaiseOutput(triggerId, "gamepad button");
                        try
                        {
                            await Task.Delay(Math.Clamp(step.DurationMs, 1, 5_000), token).ConfigureAwait(false);
                        }
                        finally
                        {
                            Release(ownerId, [], [button]);
                        }
                        break;
                    case MacroStepType.GamepadAxis:
                        lock (_outputGate)
                        {
                            ThrowIfInputPaused(token);
                            ApplyAnalog(_analogOutputs.Acquire(ownerId, step.Value ?? "Left X", step.Amount));
                        }
                        RaiseOutput(triggerId, "gamepad analog");
                        try
                        {
                            await Task.Delay(Math.Clamp(step.DurationMs, 1, 5_000), token).ConfigureAwait(false);
                        }
                        finally
                        {
                            lock (_outputGate)
                                ApplyAnalog(_analogOutputs.Release(ownerId, step.Value ?? "Left X"));
                        }
                        break;
                    case MacroStepType.LaunchProgram:
                        ExecuteInjection(token, () => LaunchProgram(step));
                        RaiseOutput(triggerId, "program");
                        break;
                    case MacroStepType.PowerShellCommand:
                        ExecuteInjection(token, () => PowerShellCommandService.Launch(step));
                        RaiseOutput(triggerId, "PowerShell");
                        break;
                    case MacroStepType.Midi:
                        ExecuteInjection(token, () => _midi.Send(step.Value ?? string.Empty));
                        RaiseOutput(triggerId, "MIDI");
                        break;
                    case MacroStepType.Osc:
                        ExecuteInjection(token, () => _osc.Send(step));
                        RaiseOutput(triggerId, "OSC");
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
        finally
        {
            ReleaseOwner(ownerId);
        }
    }

    private void Acquire(string ownerId, IEnumerable<string> keys, IEnumerable<string> buttons,
        CancellationToken token)
    {
        lock (_outputGate)
        {
            ThrowIfInputPaused(token);
            try
            {
                ApplyDelta(_outputs.Acquire(ownerId, keys, buttons));
            }
            catch
            {
                TryApplyDelta(_outputs.ReleaseOwner(ownerId));
                throw;
            }
        }
    }

    private void ExecuteInjection(CancellationToken token, Action action)
    {
        lock (_outputGate)
        {
            ThrowIfInputPaused(token);
            action();
        }
    }

    private void ThrowIfInputPaused(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_acceptingInput) throw new OperationCanceledException(token);
    }

    private static void LaunchProgram(MacroStep step)
    {
        var executable = Environment.ExpandEnvironmentVariables(step.Value?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("The program step has no executable path.");
        }
        var workingDirectory = Environment.ExpandEnvironmentVariables(step.WorkingDirectory?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(workingDirectory) && Path.IsPathFullyQualified(executable))
        {
            workingDirectory = Path.GetDirectoryName(executable) ?? string.Empty;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = step.Arguments ?? string.Empty,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });
    }

    private void StartContinuous(string ownerId, MacroDefinition macro, CancellationToken playbackToken, string triggerId)
    {
        var steps = macro.Steps.Where(step => step.Type is MacroStepType.MouseMove or MacroStepType.MouseWheel).ToArray();
        if (steps.Length == 0) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(_safety.MaximumRepeatSeconds));
        lock (_outputGate)
        {
            if (_continuousOwners.Remove(ownerId, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }
            _continuousOwners[ownerId] = cancellation;
        }
        _ = Task.Run(async () =>
        {
            var reported = false;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    foreach (var step in steps)
                    {
                        if (step.Type == MacroStepType.MouseMove) ExecuteInjection(cancellation.Token, () => MoveMouse(step));
                        else ExecuteInjection(cancellation.Token, () => ScrollMouse(step));
                        if (!reported)
                        {
                            RaiseOutput(triggerId, "mouse continuous");
                            reported = true;
                        }
                    }
                    var delay = steps.Any(step => step.Type == MacroStepType.MouseWheel)
                        ? Math.Clamp(steps.Max(step => step.DurationMs), 40, 1_000)
                        : 16;
                    await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { PlaybackError?.Invoke(this, exception.Message); }
        });
    }

    private void MoveMouse(MacroStep step)
    {
        var speed = Math.Clamp(Math.Abs(step.Amount == 0 ? 8 : step.Amount), 1, 100);
        var direction = (step.Value ?? "Right").ToUpperInvariant();
        var x = direction.Contains("LEFT") ? -speed : direction.Contains("RIGHT") ? speed : 0;
        var y = direction.Contains("UP") ? -speed : direction.Contains("DOWN") ? speed : 0;
        _input.MouseMove(x, y);
    }

    private void ScrollMouse(MacroStep step)
    {
        var amount = step.Amount == 0 ? 120 : step.Amount;
        if (string.Equals(step.Value, "Horizontal", StringComparison.OrdinalIgnoreCase))
            _input.MouseHorizontalWheel(amount);
        else
            _input.MouseWheel(amount);
    }

    private void AcquireMouseButtons(string ownerId, IEnumerable<string> buttons)
    {
        var distinct = buttons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length == 0) return;
        _heldMouseOwners[ownerId] = distinct;
        foreach (var button in distinct)
        {
            _heldMouseCounts.TryGetValue(button, out var count);
            if (count == 0) _input.MouseDown(button);
            _heldMouseCounts[button] = count + 1;
        }
    }

    private void ReleaseMouseAndContinuous(string ownerId)
    {
        if (_continuousOwners.Remove(ownerId, out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        if (!_heldMouseOwners.Remove(ownerId, out var buttons)) return;
        foreach (var button in buttons)
        {
            if (!_heldMouseCounts.TryGetValue(button, out var count)) continue;
            if (count <= 1)
            {
                _heldMouseCounts.Remove(button);
                _input.MouseUp(button);
            }
            else _heldMouseCounts[button] = count - 1;
        }
    }

    private void ReleaseAllMouseAndContinuous()
    {
        foreach (var cancellation in _continuousOwners.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        _continuousOwners.Clear();
        foreach (var button in _heldMouseCounts.Keys.ToArray())
        {
            try { _input.MouseUp(button); } catch { }
        }
        _heldMouseCounts.Clear();
        _heldMouseOwners.Clear();
    }

    private void Release(string ownerId, IEnumerable<string> keys, IEnumerable<string> buttons)
    {
        lock (_outputGate)
        {
            ApplyDelta(_outputs.Release(ownerId, keys, buttons));
        }
    }

    private void ReleaseOwner(string ownerId)
    {
        try
        {
            lock (_outputGate)
            {
                _activeHeldOwners.Remove(ownerId);
                ReleaseMouseAndContinuous(ownerId);
                ApplyDelta(_outputs.ReleaseOwner(ownerId));
                ApplyAnalog(_analogOutputs.ReleaseOwner(ownerId));
            }
        }
        catch (Exception exception)
        {
            PlaybackError?.Invoke(this, exception.Message);
        }
    }

    private void ApplyDelta(HeldOutputDelta delta)
    {
        Exception? firstFailure = null;
        try { _input.KeyDown(delta.KeysDown); }
        catch (Exception exception) { firstFailure = exception; }
        foreach (var button in delta.GamepadButtonsDown)
        {
            try { _gamepad.SetButton(button, true); }
            catch (Exception exception) { firstFailure ??= exception; }
        }
        try
        {
            _input.KeyUp(delta.KeysUp);
        }
        catch (Exception exception)
        {
            firstFailure ??= exception;
            foreach (var key in delta.KeysUp)
            {
                try { _input.KeyUp([key]); }
                catch { }
            }
        }
        foreach (var button in delta.GamepadButtonsUp)
        {
            try { _gamepad.SetButton(button, false); }
            catch (Exception exception) { firstFailure ??= exception; }
        }
        if (firstFailure is not null)
        {
            throw firstFailure;
        }
    }

    private void TryApplyDelta(HeldOutputDelta delta)
    {
        try { ApplyDelta(delta); }
        catch { }
    }

    private void ApplyAnalog(IEnumerable<GamepadAnalogChange> changes)
    {
        foreach (var change in changes) _gamepad.SetAxis(change.Axis, change.Value);
    }

    private void TryApplyAnalog(IEnumerable<GamepadAnalogChange> changes)
    {
        try { ApplyAnalog(changes); } catch { }
    }

    private void RaiseOutput(string triggerId, string kind)
    {
        try { OutputDispatched?.Invoke(this, new MacroOutputEventArgs(triggerId, kind, Stopwatch.GetTimestamp())); }
        catch { }
    }

    private static string HeldOwnerId(string triggerId) => $"held:{triggerId}";

    private static void ValidateKeys(MacroDefinition macro)
    {
        foreach (var key in macro.Steps
                     .Where(step => step.Type is MacroStepType.KeyChord or MacroStepType.KeyDown or MacroStepType.KeyUp)
                     .SelectMany(step => step.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = WindowsInputService.ResolveKey(key);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource playback;
        lock (_playbackGate)
        {
            if (_disposed) return;
            _disposed = true;
            _acceptingInput = false;
            playback = _playbackCancellation;
        }
        playback.Cancel();
        playback.Dispose();
        lock (_outputGate)
        {
            _activeHeldOwners.Clear();
            ReleaseAllMouseAndContinuous();
            TryApplyDelta(_outputs.ReleaseAll());
            TryApplyAnalog(_analogOutputs.ReleaseAll());
        }
        _midi.Dispose();
        _osc.Dispose();
    }
}
