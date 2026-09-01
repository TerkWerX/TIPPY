using Tippy.Core.Models;

namespace Tippy.Core.Input;

public sealed record PedalGestureInvocation(
    string TriggerId,
    MacroDefinition Macro,
    bool IsPressed,
    string Gesture);

public sealed class PedalGestureEngine : IDisposable
{
    private sealed class PressState(PedalBinding binding)
    {
        public PedalBinding Binding { get; } = binding;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool LongPressFired { get; set; }
        public bool PressMacroStarted { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, PressState> _pressed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _pendingTaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MacroDefinition> _toggled = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private bool _disposed;
    private TimeSpan _maximumRepeatDuration = TimeSpan.FromSeconds(20);

    public PedalGestureEngine(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public event EventHandler<PedalGestureInvocation>? Invoked;

    public void ConfigureMaximumRepeatDuration(TimeSpan duration) =>
        _maximumRepeatDuration = TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 1, 600));

    public void Press(string triggerId, PedalBinding binding)
    {
        binding = binding.Clone();
        binding.Normalize();
        if (binding.Type != PedalBindingType.Macro) return;
        PressState state;
        lock (_gate)
        {
            if (_disposed || _pressed.ContainsKey(triggerId)) return;
            state = new PressState(binding);
            _pressed[triggerId] = state;
        }

        if (binding.Gestures.Toggle)
        {
            bool isOn;
            MacroDefinition toggledMacro;
            lock (_gate)
            {
                if (_toggled.Remove(triggerId, out var existing))
                {
                    isOn = false;
                    toggledMacro = existing;
                }
                else
                {
                    isOn = true;
                    toggledMacro = binding.Macro.Clone();
                    _toggled[triggerId] = toggledMacro.Clone();
                }
            }
            toggledMacro.TriggerMode = MacroTriggerMode.WhileHeld;
            Emit(triggerId, toggledMacro, isOn, isOn ? "Toggle on" : "Toggle off");
            return;
        }

        var hasLongPress = binding.Gestures.LongPressMacro.Steps.Count > 0;
        var hasDoubleTap = binding.Gestures.DoubleTapMacro.Steps.Count > 0;
        if (!hasLongPress && !hasDoubleTap)
        {
            state.PressMacroStarted = binding.Macro.Steps.Count > 0;
            if (state.PressMacroStarted) Emit(triggerId, binding.Macro, true, "Press");
        }

        if (hasLongPress) _ = DetectLongPressAsync(triggerId, state);
        if (binding.Gestures.RepeatWhileHeld && !hasLongPress && !hasDoubleTap &&
            binding.Macro.Steps.Count > 0)
        {
            _ = RepeatAsync(triggerId, state);
        }
    }

    public void Release(string triggerId, bool synthetic = false)
    {
        PressState? state;
        lock (_gate)
        {
            if (!_pressed.Remove(triggerId, out state)) return;
        }
        state.Cancellation.Cancel();
        state.Cancellation.Dispose();
        var binding = state.Binding;

        if (!binding.Gestures.Toggle)
        {
            if (state.PressMacroStarted && binding.Macro.TriggerMode == MacroTriggerMode.WhileHeld)
                Emit(triggerId, binding.Macro, false, "Release held action");
            else if (!state.LongPressFired && !state.PressMacroStarted)
                ResolveTap(triggerId, binding);
        }

        if (!synthetic && binding.ReleaseMacro.Steps.Count > 0)
            Emit(triggerId, binding.ReleaseMacro, false, "Release action");
    }

    public void Cancel(string triggerId)
    {
        PressState? state;
        CancellationTokenSource? pending;
        MacroDefinition? toggled;
        lock (_gate)
        {
            _pressed.Remove(triggerId, out state);
            _pendingTaps.Remove(triggerId, out pending);
            _toggled.Remove(triggerId, out toggled);
        }
        state?.Cancellation.Cancel();
        state?.Cancellation.Dispose();
        pending?.Cancel();
        pending?.Dispose();
        if (state?.PressMacroStarted == true && state.Binding.Macro.TriggerMode == MacroTriggerMode.WhileHeld)
            Emit(triggerId, state.Binding.Macro, false, "Canceled held action");
        if (toggled is not null)
        {
            var macro = toggled.Clone();
            macro.TriggerMode = MacroTriggerMode.WhileHeld;
            Emit(triggerId, macro, false, "Canceled toggle");
        }
    }

    public void ReleaseAll()
    {
        string[] triggers;
        lock (_gate) triggers = _pressed.Keys.Concat(_toggled.Keys).Concat(_pendingTaps.Keys).Distinct().ToArray();
        foreach (var trigger in triggers) Cancel(trigger);
    }

    private async Task DetectLongPressAsync(string triggerId, PressState state)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(state.Binding.Gestures.LongPressThresholdMs),
                _timeProvider, state.Cancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_pressed.TryGetValue(triggerId, out var current) || !ReferenceEquals(current, state)) return;
                state.LongPressFired = true;
            }
            Emit(triggerId, state.Binding.Gestures.LongPressMacro, true, "Long press");
        }
        catch (OperationCanceledException) { }
    }

    private async Task RepeatAsync(string triggerId, PressState state)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(state.Binding.Gestures.RepeatDelayMs),
                _timeProvider, state.Cancellation.Token).ConfigureAwait(false);
            var started = _timeProvider.GetTimestamp();
            while (!state.Cancellation.IsCancellationRequested &&
                   _timeProvider.GetElapsedTime(started) < _maximumRepeatDuration)
            {
                var repeated = state.Binding.Macro.Clone();
                repeated.TriggerMode = MacroTriggerMode.PressOnce;
                Emit(triggerId, repeated, true, "Repeat");
                await Task.Delay(TimeSpan.FromMilliseconds(state.Binding.Gestures.RepeatIntervalMs),
                    _timeProvider, state.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ResolveTap(string triggerId, PedalBinding binding)
    {
        if (binding.Gestures.DoubleTapMacro.Steps.Count == 0)
        {
            if (binding.Macro.Steps.Count > 0) Emit(triggerId, AsOneShot(binding.Macro), true, "Tap");
            return;
        }

        CancellationTokenSource? firstTap;
        lock (_gate)
        {
            if (_pendingTaps.Remove(triggerId, out firstTap))
            {
                firstTap.Cancel();
                firstTap.Dispose();
            }
            else
            {
                firstTap = new CancellationTokenSource();
                _pendingTaps[triggerId] = firstTap;
                _ = CompleteSingleTapAsync(triggerId, binding, firstTap);
                return;
            }
        }
        Emit(triggerId, binding.Gestures.DoubleTapMacro, true, "Double tap");
    }

    private async Task CompleteSingleTapAsync(string triggerId, PedalBinding binding, CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(binding.Gestures.DoubleTapWindowMs),
                _timeProvider, pending.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_pendingTaps.TryGetValue(triggerId, out var current) || !ReferenceEquals(current, pending)) return;
                _pendingTaps.Remove(triggerId);
            }
            if (binding.Macro.Steps.Count > 0) Emit(triggerId, AsOneShot(binding.Macro), true, "Tap");
        }
        catch (OperationCanceledException) { }
        finally { pending.Dispose(); }
    }

    private void Emit(string triggerId, MacroDefinition macro, bool isPressed, string gesture) =>
        Invoked?.Invoke(this, new PedalGestureInvocation(triggerId, macro.Clone(), isPressed, gesture));

    private static MacroDefinition AsOneShot(MacroDefinition macro)
    {
        var clone = macro.Clone();
        clone.TriggerMode = MacroTriggerMode.PressOnce;
        return clone;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseAll();
    }
}
