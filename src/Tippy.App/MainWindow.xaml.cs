using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Tippy.App.Models;
using Tippy.App.Services;
using Tippy.Core.Input;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class MainWindow : Window
{
    [Flags]
    private enum InputSuspensionReason
    {
        None = 0,
        SessionLocked = 1,
        RemoteDisconnected = 2,
        ConsoleDisconnected = 4,
        Power = 8
    }

    private readonly ProfileStore _profileStore = new();
    private readonly PedalHidService _hid = new();
    private readonly RawInputService _rawInput = new();
    private readonly VirtualGamepadService _gamepad = new();
    private readonly MacroPlayer _macroPlayer;
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly GlobalHotkeyService _emergencyHotkey = new();
    private readonly ForegroundApplicationService _foregroundApplications = new();
    private readonly PedalBankResolver _bankResolver = new();
    private readonly PedalGestureEngine _gestureEngine = new();
    private readonly PedalPatternEngine _patternEngine = new();
    private readonly PedalActivityHub _pedalActivity = new();
    private readonly PedalRegistryService _pedalRegistry = new();
    private readonly WindowPlacementService _windowPlacement = new();
    private readonly Dictionary<string, PedalDeviceInfo> _connected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string DeviceKey, int SwitchIndex), Border> _switchTiles = new();
    private readonly Dictionary<string, Border> _pedalTabHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string DeviceKey, int SwitchIndex)> _pressedSwitches = [];
    private readonly HashSet<string> _artworkPickerQueued = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(PedalDeviceProfile Profile, PedalDeviceInfo Info)> _pendingArtworkPickers = new();
    private readonly List<Button> _bankButtons = [];
    private readonly object _inputStateGate = new();
    private AppProfile _profile = new();
    private CancellationTokenSource? _saveDebounce;
    private bool _loaded;
    private bool _updatingLayoutSelection;
    private bool _buildingTabbedLayout;
    private volatile bool _inputSuspended;
    private InputSuspensionReason _inputSuspensionReasons;
    private int _inputStateGeneration;
    private string _lastOptimizationKey = string.Empty;
    private bool? _lastAutoSideBySide;
    private Point _pedalDragStart;
    private string? _draggedPedalKey;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _trayTipShown;
    private string? _activeApplicationProfileId;
    private bool _artworkPickerOpen;
    private PedalDiagnosticsWindow? _diagnosticsWindow;
    private StatusOverlayWindow? _overlayWindow;
    private bool _rehearsalMode;
    private string? _startupProfileError;
    private WindowState _windowStateBeforeTray = WindowState.Normal;
    private bool _restoreMaximizedOnLoad;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            _profile = _profileStore.LoadDefaultAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _profile = new AppProfile();
            _startupProfileError = exception.Message;
        }
        _profile.Normalize();
        _restoreMaximizedOnLoad = _profile.WindowPlacement.IsMaximized;
        InitializeTrayIcon();
        _macroPlayer = new MacroPlayer(new WindowsInputService(), _gamepad);
        _macroPlayer.ConfigureMidi(_profile.Midi);
        _gestureEngine.Invoked += GestureEngine_Invoked;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        SizeChanged += MainWindow_SizeChanged;
        LocationChanged += MainWindow_LocationChanged;
        StateChanged += MainWindow_StateChanged;
        SourceInitialized += MainWindow_SourceInitialized;
        _hid.ConnectionChanged += Hid_ConnectionChanged;
        _hid.StateChanged += Hid_StateChanged;
        _hid.ScanCompleted += Hid_ScanCompleted;
        _hid.Diagnostic += (_, message) => Dispatcher.Invoke(() => SetStatus(message));
        _rawInput.KeyChanged += RawInput_KeyChanged;
        _rawInput.DevicesChanged += (_, _) => Dispatcher.BeginInvoke(new Action(SyncRawInputDevices));
        _macroPlayer.PlaybackError += (_, error) => Dispatcher.Invoke(() => SetStatus(error, true));
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupProfileError is not null)
        {
            MessageBox.Show(this, $"The default profile could not be loaded. A fresh profile is active.\n\n{_startupProfileError}",
                "Tippy profile", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _macroPlayer.ConfigureSafety(_profile.Safety);
        _gestureEngine.ConfigureMaximumRepeatDuration(TimeSpan.FromSeconds(_profile.Safety.MaximumRepeatSeconds));
        _patternEngine.Configure(_profile.PedalPatterns);
        _pedalRegistry.Reload();
        ThemeService.Apply(_profile.Theme);
        SetLayoutSelector();
        _loaded = true;
        if (_restoreMaximizedOnLoad)
        {
            WindowState = WindowState.Maximized;
            _windowStateBeforeTray = WindowState.Maximized;
        }
        if (_profile.WindowPlacement.HasPlacement && !_profile.IsSubCompactMode)
            _lastOptimizationKey = GetWindowOptimizationKey();
        RememberWindowPlacement();
        ScheduleSave();
        BuildBankButtons();
        RefreshDevices(_profile.IsSubCompactMode);
        UpdateHeader();
        RegisterHotkeys();
        _hid.ConfigureLearnedDevices(_profile.LearnedPedals);
        SyncRawInputDevices();
        _hid.Start();
        SetStatus("Listening for USB foot controls");
        if (_profile.StartMinimized)
        {
            _ = Dispatcher.BeginInvoke(new Action(HideToTray));
        }
    }

    private void Hid_ConnectionChanged(object? sender, PedalConnectionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.IsConnected)
            {
                _pedalRegistry.Reload();
                _connected[e.Device.DeviceKey] = e.Device;
                var deviceProfile = _profile.Devices.FirstOrDefault(device =>
                    device.DeviceKey.Equals(e.Device.DeviceKey, StringComparison.OrdinalIgnoreCase));
                if (deviceProfile is null)
                {
                    deviceProfile = PedalDeviceProfile.Create(e.Device.DeviceKey, e.Device.DisplayName,
                        e.Device.VendorId, e.Device.ProductId, e.Device.SwitchCount);
                    _profile.Devices.Add(deviceProfile);
                    ScheduleSave();
                }
                else if (deviceProfile.SwitchCount != e.Device.SwitchCount ||
                         !deviceProfile.DisplayName.Equals(e.Device.DisplayName, StringComparison.Ordinal))
                {
                    deviceProfile.DisplayName = e.Device.DisplayName;
                    deviceProfile.SwitchCount = e.Device.SwitchCount;
                    deviceProfile.Normalize();
                    ScheduleSave();
                }
                var needsAmbiguousChoice = string.IsNullOrWhiteSpace(deviceProfile.ArtworkKey) &&
                                           _pedalRegistry.IsAmbiguous(e.Device);
                if (string.IsNullOrWhiteSpace(deviceProfile.ArtworkKey))
                {
                    deviceProfile.ArtworkKey = _pedalRegistry.ResolveArtwork(string.Empty, e.Device)?.Key ?? string.Empty;
                    ScheduleSave();
                }
                if (needsAmbiguousChoice && _artworkPickerQueued.Add(deviceProfile.DeviceKey))
                {
                    QueueArtworkPicker(deviceProfile, e.Device);
                }
            }
            else
            {
                ReleaseActionsForDevice(e.Device.DeviceKey);
                _connected.Remove(e.Device.DeviceKey);
            }
            RefreshDevices(true);
            _diagnosticsWindow?.SetDevices(_connected.Values.ToArray());
            SetStatus($"{_connected.Count} foot control{(_connected.Count == 1 ? string.Empty : "s")} connected");
        });
    }

    private void Hid_ScanCompleted(object? sender, EventArgs e)
    {
        _ = Task.Run(() =>
        {
            try
            {
                _pedalRegistry.AuditCandidates(new HidLearningService().ListCandidates());
            }
            catch (Exception exception)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    SetStatus($"Pedal library audit failed: {exception.Message}", true)));
            }
        });
    }

    private void Hid_StateChanged(object? sender, PedalStateEventArgs e)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
        {
            if (_inputSuspended) return;
            _diagnosticsWindow?.Record(e, System.Diagnostics.Stopwatch.GetTimestamp());
            var triggerId = $"{e.Device.DeviceKey}:{e.SwitchIndex}";
            var key = (e.Device.DeviceKey, e.SwitchIndex);
            ApplicationProfileRule? applicationProfile = null;
            if (e.IsPressed)
            {
                applicationProfile = ResolveForegroundApplicationProfile();
                SetActiveApplicationProfile(applicationProfile);
            }

            RawReportText.Text = $"{e.Device.DisplayName}: {Convert.ToHexString(e.RawReport)}";
            if (e.IsPressed) _pressedSwitches.Add(key);
            else _pressedSwitches.Remove(key);
            if (e.IsPressed && _profile.IsSubCompactMode &&
                !e.Device.DeviceKey.Equals(_profile.SelectedTabbedDeviceKey, StringComparison.OrdinalIgnoreCase))
            {
                _profile.SelectedTabbedDeviceKey = e.Device.DeviceKey;
                RefreshDevices();
                ScheduleSave();
            }
            UpdatePressedVisual(key, e.IsPressed);

            if (!e.IsPressed)
            {
                _patternEngine.Release(triggerId);
                _gestureEngine.Release(triggerId, e.IsSynthetic);
                var releasedShift = _bankResolver.ReleaseShift(e.Device.DeviceKey, e.SwitchIndex);
                if (releasedShift)
                {
                    RefreshDevices();
                    var releasedDevice = _profile.Devices.FirstOrDefault(profileDevice =>
                        profileDevice.DeviceKey.Equals(e.Device.DeviceKey, StringComparison.OrdinalIgnoreCase));
                    if (releasedDevice is not null)
                    {
                        SetStatus($"{releasedDevice.DisplayName} · momentary layer released · Bank {GetEffectiveBankIndex(releasedDevice) + 1}");
                    }
                }
                return;
            }

            var device = _profile.Devices.FirstOrDefault(profileDevice =>
                profileDevice.DeviceKey.Equals(e.Device.DeviceKey, StringComparison.OrdinalIgnoreCase));
            if (device is null || e.SwitchIndex >= device.SwitchCount)
            {
                return;
            }
            var bankIndex = _bankResolver.Resolve(device, applicationProfile);
            var binding = device.Banks[bankIndex].Bindings[e.SwitchIndex];
            _pedalActivity.Publish(device.DeviceKey, device.DisplayName, e.SwitchIndex);
            foreach (var pattern in _patternEngine.Press(triggerId, System.Diagnostics.Stopwatch.GetTimestamp()))
            {
                var patternMacro = PrepareMacro(pattern.Macro, device, e.SwitchIndex, bankIndex, applicationProfile);
                if (!_rehearsalMode) _macroPlayer.Handle($"pattern:{pattern.PatternId}", patternMacro, true);
                SetStatus($"{(_rehearsalMode ? "Rehearsal · would run" : "Foot pattern")} · {pattern.Name}");
                ShowOverlay(pattern.Name, $"Foot {pattern.Type.ToString().ToLowerInvariant()} · {device.DisplayName}");
            }
            switch (binding.Type)
            {
                case PedalBindingType.BankNext:
                    if (_rehearsalMode)
                    {
                        SetStatus($"Rehearsal · would switch {device.DisplayName} to the next bank");
                        ShowOverlay("Next bank", $"Would switch · {device.DisplayName}");
                    }
                    else SwitchPedalBank(device, (device.ActiveBankIndex + 1) % AppProfile.MaxBanks);
                    break;
                case PedalBindingType.ShiftLayer:
                    if (_rehearsalMode)
                    {
                        SetStatus($"Rehearsal · would hold Bank {binding.ShiftBankIndex + 1} on {device.DisplayName}");
                        ShowOverlay($"Shift Bank {binding.ShiftBankIndex + 1}", $"Would hold · {device.DisplayName}");
                        break;
                    }
                    _bankResolver.ActivateShift(device.DeviceKey, e.SwitchIndex, binding.ShiftBankIndex);
                    RefreshDevices();
                    UpdatePressedVisual(key, true);
                    SetStatus($"{device.DisplayName} · momentary Bank {binding.ShiftBankIndex + 1} active while held");
                    break;
                case PedalBindingType.Macro:
                    _gestureEngine.Press(triggerId,
                        PrepareBinding(binding, device, e.SwitchIndex, bankIndex, applicationProfile));
                    var context = applicationProfile is null ? string.Empty : $" · {applicationProfile.Name}";
                    SetStatus($"{e.Device.DisplayName} · Bank {bankIndex + 1}{context} · Pedal {e.SwitchIndex + 1} · {binding.DisplayName}");
                    break;
            }
        }));
    }

    private void GestureEngine_Invoked(object? sender, PedalGestureInvocation invocation)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
        {
            if (_inputSuspended) return;
            if (!_rehearsalMode) _macroPlayer.Handle(invocation.TriggerId, invocation.Macro, invocation.IsPressed);
            if (!invocation.Gesture.StartsWith("Release held", StringComparison.OrdinalIgnoreCase))
                SetStatus($"{(_rehearsalMode ? "Rehearsal · would run" : invocation.Gesture)} · {invocation.Macro.Name}");
            ShowOverlay(invocation.Macro.Name, _rehearsalMode ? $"Would run · {invocation.Gesture}" : invocation.Gesture);
        }));
    }

    private PedalBinding PrepareBinding(
        PedalBinding source,
        PedalDeviceProfile device,
        int switchIndex,
        int bankIndex,
        ApplicationProfileRule? applicationProfile)
    {
        var result = source.Clone();
        result.Macro = PrepareMacro(result.Macro, device, switchIndex, bankIndex, applicationProfile);
        result.ReleaseMacro = PrepareMacro(result.ReleaseMacro, device, switchIndex, bankIndex, applicationProfile);
        result.Gestures.DoubleTapMacro = PrepareMacro(result.Gestures.DoubleTapMacro, device, switchIndex, bankIndex, applicationProfile);
        result.Gestures.LongPressMacro = PrepareMacro(result.Gestures.LongPressMacro, device, switchIndex, bankIndex, applicationProfile);
        return result;
    }

    private MacroDefinition PrepareMacro(
        MacroDefinition source,
        PedalDeviceProfile device,
        int switchIndex,
        int bankIndex,
        ApplicationProfileRule? applicationProfile)
    {
        var foreground = applicationProfile?.Name ?? _foregroundApplications.GetCurrent()?.ProcessName ?? string.Empty;
        var clipboard = string.Empty;
        try { if (Clipboard.ContainsText()) clipboard = Clipboard.GetText(); } catch { }
        return MacroVariableExpander.Expand(source, new MacroVariableContext(
            _profile.Name, device.DisplayName, switchIndex + 1, bankIndex + 1, foreground, clipboard),
            _profile.Variables);
    }

    private void UpdatePressedVisual((string DeviceKey, int SwitchIndex) key, bool isPressed)
    {
        if (_switchTiles.TryGetValue(key, out var tile))
        {
            var generic = Equals(tile.Tag, "GenericSwitch");
            tile.Background = isPressed
                ? new SolidColorBrush(Color.FromArgb(145, 70, 205, 255))
                : generic ? (Brush)FindResource("SurfaceBrush") : Brushes.Transparent;
            tile.BorderBrush = isPressed
                ? new SolidColorBrush(Color.FromRgb(86, 220, 255))
                : generic ? (Brush)FindResource("BorderBrush") : Brushes.Transparent;
            tile.BorderThickness = generic && !isPressed ? new Thickness(2) : new Thickness(4);
            tile.RenderTransform = isPressed ? new ScaleTransform(0.985, 0.985) : Transform.Identity;
            tile.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        if (_pedalTabHeaders.TryGetValue(key.DeviceKey, out var tabHeader))
        {
            var anyPressed = _pressedSwitches.Any(item =>
                item.DeviceKey.Equals(key.DeviceKey, StringComparison.OrdinalIgnoreCase));
            tabHeader.Background = anyPressed
                ? (Brush)FindResource("AccentSoftBrush")
                : Brushes.Transparent;
        }
    }

    private ApplicationProfileRule? ResolveForegroundApplicationProfile()
    {
        var foreground = _foregroundApplications.GetCurrent();
        if (foreground is null) return null;
        return _profile.ApplicationProfiles.FirstOrDefault(profile =>
            profile.Matches(foreground.ProcessName, foreground.ExecutablePath));
    }

    private void SetActiveApplicationProfile(ApplicationProfileRule? profile)
    {
        var id = profile?.Id;
        if (string.Equals(_activeApplicationProfileId, id, StringComparison.OrdinalIgnoreCase)) return;
        _activeApplicationProfileId = id;
        RefreshDevices();
        UpdateHeader();
        ShowOverlay(profile?.Name ?? "Default banks",
            profile is null ? "No foreground application profile" : "Foreground application profile active");
    }

    private ApplicationProfileRule? GetActiveApplicationProfile() =>
        string.IsNullOrWhiteSpace(_activeApplicationProfileId)
            ? null
            : _profile.ApplicationProfiles.FirstOrDefault(profile =>
                profile.Id.Equals(_activeApplicationProfileId, StringComparison.OrdinalIgnoreCase));

    private int GetEffectiveBankIndex(PedalDeviceProfile device) =>
        _bankResolver.Resolve(device, GetActiveApplicationProfile());

    private void ReleaseActionsForDevice(string deviceKey)
    {
        _bankResolver.ReleaseDevice(deviceKey);
        foreach (var key in _pressedSwitches
                     .Where(key => key.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var triggerId = $"{key.DeviceKey}:{key.SwitchIndex}";
            _gestureEngine.Cancel(triggerId);
            _macroPlayer.ReleaseHeld(triggerId);
            _patternEngine.Release(triggerId);
        }
        _pressedSwitches.RemoveWhere(key => key.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
    }

    private void BuildBankButtons()
    {
        BankButtonsPanel.Children.Clear();
        _bankButtons.Clear();
        for (var index = 0; index < AppProfile.MaxBanks; index++)
        {
            var bankIndex = index;
            var button = new Button
            {
                Content = $"{index + 1}   BANK {index + 1}", Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(18, 9, 18, 9), FontWeight = FontWeights.SemiBold
            };
            button.Click += (_, _) => SwitchAllBanks(bankIndex);
            _bankButtons.Add(button);
            BankButtonsPanel.Children.Add(button);
        }
        UpdateBankButtons();
    }

    private void SwitchAllBanks(int bankIndex)
    {
        bankIndex = Math.Clamp(bankIndex, 0, AppProfile.MaxBanks - 1);
        _profile.ActiveBankIndex = bankIndex;
        foreach (var device in _profile.Devices) device.ActiveBankIndex = bankIndex;
        UpdateBankButtons();
        RefreshDevices();
        ScheduleSave();
        SetStatus($"Bank {bankIndex + 1} active on all pedals");
        ShowOverlay($"Bank {bankIndex + 1}", "All pedals");
    }

    private void SwitchPedalBank(PedalDeviceProfile device, int bankIndex)
    {
        device.ActiveBankIndex = Math.Clamp(bankIndex, 0, AppProfile.MaxBanks - 1);
        UpdateBankButtons();
        RefreshDevices();
        ScheduleSave();
        SetStatus($"{device.DisplayName} · Bank {device.ActiveBankIndex + 1} active");
        ShowOverlay($"Bank {device.ActiveBankIndex + 1}", device.DisplayName);
    }

    private void UpdateBankButtons()
    {
        for (var index = 0; index < _bankButtons.Count; index++)
        {
            var active = _profile.Devices.Count > 0 && _profile.Devices.All(device => device.ActiveBankIndex == index);
            _bankButtons[index].Background = (Brush)FindResource(active ? "AccentBrush" : "SurfaceBrush");
            _bankButtons[index].Foreground = active ? Brushes.Black : (Brush)FindResource("TextBrush");
            _bankButtons[index].BorderBrush = (Brush)FindResource(active ? "AccentBrush" : "BorderBrush");
        }
    }

    private void RefreshDevices(bool optimizeWindow = false)
    {
        if (!_loaded) return;
        DevicesPanel.Children.Clear();
        DevicesPanel.RowDefinitions.Clear();
        DevicesPanel.ColumnDefinitions.Clear();
        _switchTiles.Clear();
        _pedalTabHeaders.Clear();
        SubCompactPedalHost.Children.Clear();
        SubCompactDotsPanel.Children.Clear();
        EmptyState.Visibility = _profile.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var count = _profile.Devices.Count;

        ApplyLayoutChrome();
        if (_profile.IsSubCompactMode && count > 0)
        {
            BuildSubCompactDeviceLayout();
        }
        else if (UsesTabbedPedalPresentation() && count > 0)
        {
            BuildTabbedDeviceLayout();
        }
        else
        {
            BuildGridDeviceLayout();
        }

        foreach (var pressed in _pressedSwitches) UpdatePressedVisual(pressed, true);
        StatusDot.Fill = (Brush)FindResource(_connected.Count > 0 ? "SuccessBrush" : "MutedTextBrush");
        if (optimizeWindow) OptimizeWindowForPedals();
    }

    private void BuildGridDeviceLayout()
    {
        var count = _profile.Devices.Count;
        var columns = GetGridColumnCount(count);
        var rows = Math.Max(1, (int)Math.Ceiling(count / (double)columns));
        _lastAutoSideBySide = columns > 1;
        for (var column = 0; column < columns; column++)
            DevicesPanel.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < rows; row++)
            DevicesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < count; index++)
        {
            var device = _profile.Devices[index];
            device.Normalize();
            var row = index / columns;
            var column = index % columns;
            var card = CreateDeviceCard(device, _connected.ContainsKey(device.DeviceKey), false);
            card.Margin = new Thickness(
                column == 0 ? 0 : 7,
                row == 0 ? 0 : 7,
                column == columns - 1 || index == count - 1 ? 0 : 7,
                row == rows - 1 ? 14 : 7);
            Grid.SetRow(card, row);
            Grid.SetColumn(card, column);
            DevicesPanel.Children.Add(card);
        }
    }

    private void BuildTabbedDeviceLayout()
    {
        _buildingTabbedLayout = true;
        var tabs = new TabControl
        {
            Background = Brushes.Transparent,
            BorderBrush = (Brush)FindResource("BorderBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        tabs.SelectionChanged += TabbedDevices_SelectionChanged;

        foreach (var device in _profile.Devices)
        {
            device.Normalize();
            var connected = _connected.ContainsKey(device.DeviceKey);
            var header = CreatePedalTabHeader(device, connected);
            var tab = new TabItem
            {
                Header = header,
                Content = CreateDeviceCard(device, connected, true),
                Tag = device.DeviceKey,
                AllowDrop = true
            };
            tab.DragOver += (_, args) => PedalTab_DragOver(header, device.DeviceKey, args);
            tab.DragLeave += (_, _) => ResetPedalTabHeader(header);
            tab.Drop += (_, args) => PedalTab_Drop(header, device.DeviceKey, args);
            tabs.Items.Add(tab);
        }

        var selectedIndex = _profile.Devices.FindIndex(device =>
            device.DeviceKey.Equals(_profile.SelectedTabbedDeviceKey, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
            selectedIndex = _profile.Devices.FindIndex(device => _connected.ContainsKey(device.DeviceKey));
        tabs.SelectedIndex = Math.Max(0, selectedIndex);
        if (tabs.SelectedItem is TabItem selected && selected.Tag is string key)
            _profile.SelectedTabbedDeviceKey = key;
        _buildingTabbedLayout = false;
        DevicesPanel.Children.Add(tabs);
    }

    private void BuildSubCompactDeviceLayout()
    {
        var selectedIndex = _profile.Devices.FindIndex(device =>
            device.DeviceKey.Equals(_profile.SelectedTabbedDeviceKey, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
            selectedIndex = _profile.Devices.FindIndex(device => _connected.ContainsKey(device.DeviceKey));
        selectedIndex = Math.Max(0, selectedIndex);
        var selected = _profile.Devices[selectedIndex];
        _profile.SelectedTabbedDeviceKey = selected.DeviceKey;
        SubCompactPedalHost.Children.Add(CreateSubCompactPedalVisual(selected,
            _connected.ContainsKey(selected.DeviceKey)));

        for (var index = 0; index < _profile.Devices.Count; index++)
        {
            var device = _profile.Devices[index];
            var isSelected = index == selectedIndex;
            var dot = new Border
            {
                Width = 15, Height = 15, Background = Brushes.Transparent,
                Cursor = Cursors.Hand, ToolTip = device.DisplayName, Tag = "SubCompactDot",
                Child = new Ellipse
                {
                    Width = isSelected ? 8 : 6, Height = isSelected ? 8 : 6,
                    Fill = (Brush)FindResource(isSelected ? "AccentBrush" : "MutedTextBrush")
                }
            };
            AutomationProperties.SetName(dot, $"Show pedal {index + 1}");
            dot.MouseLeftButtonUp += (_, _) =>
            {
                _profile.SelectedTabbedDeviceKey = device.DeviceKey;
                RefreshDevices();
                ScheduleSave();
            };
            SubCompactDotsPanel.Children.Add(dot);
        }
    }

    private Border CreatePedalTabHeader(PedalDeviceProfile device, bool connected)
    {
        var text = new TextBlock
        {
            Text = $"{(connected ? "●" : "○")}  {device.DisplayName}",
            Foreground = new SolidColorBrush(connected
                ? Color.FromRgb(17, 24, 32)
                : Color.FromRgb(82, 94, 108)),
            FontWeight = FontWeights.SemiBold
        };
        var header = new Border
        {
            Tag = device.DeviceKey,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Select this pedal, or drag to reorder it",
            Child = text
        };
        header.PreviewMouseLeftButtonDown += PedalHandle_PreviewMouseLeftButtonDown;
        header.PreviewMouseMove += PedalHandle_PreviewMouseMove;
        _pedalTabHeaders[device.DeviceKey] = header;
        return header;
    }

    private Border CreateDeviceCard(PedalDeviceProfile device, bool connected, bool compact)
    {
        var shell = new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"), BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
            Padding = compact ? new Thickness(10) : new Thickness(18),
            Margin = compact ? new Thickness(6) : new Thickness(0), AllowDrop = true
        };
        shell.DragOver += (_, args) => PedalCard_DragOver(shell, device.DeviceKey, args);
        shell.DragLeave += (_, _) =>
        {
            shell.BorderBrush = (Brush)FindResource("BorderBrush");
            shell.BorderThickness = new Thickness(1);
        };
        shell.Drop += (_, args) => PedalCard_Drop(shell, device.DeviceKey, args);
        var stack = new StackPanel();
        shell.Child = stack;
        var header = new Grid { Margin = new Thickness(0, 0, 0, compact ? 8 : 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grabHandle = new Border
        {
            Tag = device.DeviceKey, Background = (Brush)FindResource("SurfaceAltBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(0, 0, 11, 0), Cursor = System.Windows.Input.Cursors.SizeAll,
            VerticalAlignment = VerticalAlignment.Top, ToolTip = "Drag to change this pedal's position",
            Child = new TextBlock
            {
                Text = "⠿", FontSize = 19, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("MutedTextBrush")
            }
        };
        grabHandle.PreviewMouseLeftButtonDown += PedalHandle_PreviewMouseLeftButtonDown;
        grabHandle.PreviewMouseMove += PedalHandle_PreviewMouseMove;
        header.Children.Add(grabHandle);
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = device.DisplayName, FontSize = 17, FontWeight = FontWeights.SemiBold });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"VID_{device.VendorId:X4} · PID_{device.ProductId:X4} · {ShortDeviceKey(device.DeviceKey)}",
            Style = (Style)FindResource("SmallMutedText"), Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);
        var badge = new Border
        {
            Background = (Brush)FindResource(connected ? "AccentSoftBrush" : "SurfaceAltBrush"), CornerRadius = new CornerRadius(20),
            Padding = new Thickness(10, 5, 10, 5), Child = new TextBlock
            {
                Text = connected ? "●  CONNECTED" : "○  DISCONNECTED",
                Foreground = (Brush)FindResource(connected ? "SuccessBrush" : "MutedTextBrush"),
                FontSize = 11, FontWeight = FontWeights.Bold
            }
        };
        var headerTools = new StackPanel { Orientation = Orientation.Horizontal };
        var pictureButton = new Button
        {
            Content = "Picture", Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0), ToolTip = "Choose the artwork used for this pedal"
        };
        pictureButton.Click += (_, _) =>
        {
            var info = GetDeviceInfo(device);
            ShowArtworkPicker(device, info, _pedalRegistry.IsAmbiguous(info));
        };
        headerTools.Children.Add(pictureButton);
        headerTools.Children.Add(badge);
        Grid.SetColumn(headerTools, 2);
        header.Children.Add(headerTools);
        if (!_profile.IsCompactMode) stack.Children.Add(header);
        stack.Children.Add(CreateDeviceBankBar(device, compact));
        stack.Children.Add(CreatePedalVisual(device, connected));
        return shell;
    }

    private FrameworkElement CreateDeviceBankBar(PedalDeviceProfile device, bool compact)
    {
        var effectiveBankIndex = GetEffectiveBankIndex(device);
        var applicationProfile = GetActiveApplicationProfile();
        var isShifted = _bankResolver.IsShifted(device.DeviceKey);
        var bar = new Border
        {
            Background = (Brush)FindResource("SurfaceAltBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(0, 0, 0, compact ? 6 : 10)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Child = grid;

        var bankPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bankPanel.Children.Add(new TextBlock
        {
            Text = isShifted ? "SHIFT BANK" : applicationProfile is null ? "BANK" : "APP BANK",
            ToolTip = isShifted
                ? "A momentary layer is active"
                : applicationProfile is null ? null : $"{applicationProfile.Name} foreground profile",
            Style = (Style)FindResource("SmallMutedText"), FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0)
        });
        for (var index = 0; index < AppProfile.MaxBanks; index++)
        {
            var bankIndex = index;
            var active = effectiveBankIndex == index;
            var button = new Button
            {
                Content = (index + 1).ToString(), Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 5, 0), ToolTip = device.Banks[index].Name,
                Background = (Brush)FindResource(active ? "AccentBrush" : "SurfaceBrush"),
                Foreground = active ? Brushes.Black : (Brush)FindResource("TextBrush"),
                BorderBrush = (Brush)FindResource(active ? "AccentBrush" : "BorderBrush")
            };
            button.Click += (_, _) => SwitchPedalBank(device, bankIndex);
            bankPanel.Children.Add(button);
        }
        grid.Children.Add(bankPanel);

        if (!_profile.IsCompactMode)
        {
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            var save = new Button { Content = "Save bank", Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 5, 0) };
            save.Click += async (_, _) => await SavePedalBankAsync(device);
            var load = new Button { Content = "Load bank", Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 5, 0) };
            load.Click += async (_, _) => await LoadPedalBankAsync(device);
            var copy = new Button { Content = "Copy to…", Padding = new Thickness(9, 4, 9, 4), ToolTip = "Copy this bank to one or more compatible pedals" };
            copy.Click += (_, _) => CopyPedalBank(device);
            tools.Children.Add(save);
            tools.Children.Add(load);
            tools.Children.Add(copy);
            Grid.SetColumn(tools, 1);
            grid.Children.Add(tools);
        }
        return bar;
    }

    private async Task SavePedalBankAsync(PedalDeviceProfile device)
    {
        var bank = device.Banks[device.ActiveBankIndex];
        var dialog = new SaveFileDialog
        {
            Filter = "Tippy bank (*.tippy-bank.json)|*.tippy-bank.json",
            FileName = $"{SafeFileName(bank.Name)}.tippy-bank.json",
            AddExtension = true, DefaultExt = ".tippy-bank.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await PedalBankStore.SaveAsync(dialog.FileName, new SavedPedalBank
            {
                Name = bank.Name,
                RequiredSwitchCount = device.SwitchCount,
                Bank = bank.Clone()
            });
            SetStatus($"Saved {bank.Name} from {device.DisplayName}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save bank", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadPedalBankAsync(PedalDeviceProfile invokingDevice)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Tippy bank (*.tippy-bank.json)|*.tippy-bank.json|JSON files (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var saved = await PedalBankStore.LoadAsync(dialog.FileName);
            ChooseTargetsAndApply(saved, invokingDevice.DeviceKey);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not load bank", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyPedalBank(PedalDeviceProfile source)
    {
        var bank = source.Banks[source.ActiveBankIndex];
        ChooseTargetsAndApply(new SavedPedalBank
        {
            Name = bank.Name,
            RequiredSwitchCount = source.SwitchCount,
            Bank = bank.Clone()
        }, null);
    }

    private void ChooseTargetsAndApply(SavedPedalBank saved, string? initiallySelectedDeviceKey)
    {
        saved.Normalize();
        var compatible = _profile.Devices.Where(device =>
                _connected.ContainsKey(device.DeviceKey) && device.SwitchCount >= saved.RequiredSwitchCount)
            .ToArray();
        if (compatible.Length == 0)
        {
            MessageBox.Show(this,
                $"No connected pedal has the {saved.RequiredSwitchCount} switches required by this bank.",
                "No compatible pedal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var chooser = new BankTargetWindow(saved.Name, saved.RequiredSwitchCount, compatible, initiallySelectedDeviceKey)
        {
            Owner = this
        };
        if (chooser.ShowDialog() != true) return;
        foreach (var target in chooser.SelectedDevices)
        {
            var clone = saved.Bank.Clone();
            clone.EnsureSwitchCount(target.SwitchCount);
            target.Banks[target.ActiveBankIndex] = clone;
        }
        RefreshDevices();
        ScheduleSave();
        SetStatus($"Loaded {saved.Name} into {chooser.SelectedDevices.Count} pedal{(chooser.SelectedDevices.Count == 1 ? string.Empty : "s")}");
    }

    private void PedalHandle_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _pedalDragStart = e.GetPosition(this);
        _draggedPedalKey = (sender as FrameworkElement)?.Tag as string;
    }

    private void PedalHandle_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || string.IsNullOrWhiteSpace(_draggedPedalKey))
            return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _pedalDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _pedalDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        var key = _draggedPedalKey;
        _draggedPedalKey = null;
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender,
            new DataObject("TippyPedalDeviceKey", key), DragDropEffects.Move);
    }

    private void PedalCard_DragOver(Border card, string targetKey, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TippyPedalDeviceKey") ||
            e.Data.GetData("TippyPedalDeviceKey") is not string sourceKey ||
            sourceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        card.BorderBrush = (Brush)FindResource("AccentBrush");
        card.BorderThickness = new Thickness(2);
        e.Handled = true;
    }

    private void PedalCard_Drop(Border card, string targetKey, DragEventArgs e)
    {
        card.BorderBrush = (Brush)FindResource("BorderBrush");
        card.BorderThickness = new Thickness(1);
        if (e.Data.GetData("TippyPedalDeviceKey") is not string sourceKey ||
            sourceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
            return;
        var sourceIndexBeforeMove = _profile.Devices.FindIndex(device =>
            device.DeviceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase));
        var targetIndexBeforeMove = _profile.Devices.FindIndex(device =>
            device.DeviceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (sourceIndexBeforeMove < 0 || targetIndexBeforeMove < 0) return;
        var position = e.GetPosition(card);
        var insertAfter = _profile.Devices.Count == 2
            ? sourceIndexBeforeMove < targetIndexBeforeMove
            : GetGridColumnCount(_profile.Devices.Count) > 1
                ? position.X >= card.ActualWidth / 2
                : position.Y >= card.ActualHeight / 2;
        MovePedal(sourceKey, targetKey, insertAfter);
        e.Handled = true;
    }

    private void PedalTab_DragOver(Border header, string targetKey, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TippyPedalDeviceKey") ||
            e.Data.GetData("TippyPedalDeviceKey") is not string sourceKey ||
            sourceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        header.BorderBrush = (Brush)FindResource("AccentBrush");
        e.Handled = true;
    }

    private void PedalTab_Drop(Border header, string targetKey, DragEventArgs e)
    {
        ResetPedalTabHeader(header);
        if (e.Data.GetData("TippyPedalDeviceKey") is not string sourceKey ||
            sourceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
            return;
        var position = e.GetPosition(header);
        MovePedal(sourceKey, targetKey, position.X >= header.ActualWidth / 2);
        e.Handled = true;
    }

    private void ResetPedalTabHeader(Border header)
    {
        header.BorderBrush = Brushes.Transparent;
        header.BorderThickness = new Thickness(1);
    }

    private void MovePedal(string sourceKey, string targetKey, bool insertAfter)
    {
        var source = _profile.Devices.FirstOrDefault(device =>
            device.DeviceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase));
        var target = _profile.Devices.FirstOrDefault(device =>
            device.DeviceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (source is null || target is null) return;
        _profile.Devices.Remove(source);
        var targetIndex = _profile.Devices.IndexOf(target);
        _profile.Devices.Insert(Math.Clamp(targetIndex + (insertAfter ? 1 : 0), 0, _profile.Devices.Count), source);
        RefreshDevices();
        ScheduleSave();
        SetStatus($"Moved {source.DisplayName} {(insertAfter ? "after" : "before")} {target.DisplayName}");
    }

    private FrameworkElement CreatePedalVisual(PedalDeviceProfile device, bool connected)
    {
        var artwork = _pedalRegistry.ResolveArtwork(device.ArtworkKey, GetDeviceInfo(device));
        if (artwork is null || string.IsNullOrWhiteSpace(artwork.ImagePath))
        {
            return CreateGenericPedalVisual(device, connected, artwork?.ModelLabel);
        }

        var canvas = new Grid
        {
            Width = 760,
            Height = 485,
            Opacity = connected ? 1 : 0.58
        };
        try
        {
            var bitmap = LoadArtworkBitmap(artwork.ImagePath);
            bitmap.Freeze();
            canvas.Children.Add(new Image
            {
                Source = bitmap, Stretch = Stretch.Uniform, SnapsToDevicePixels = true
            });
        }
        catch
        {
            return CreateGenericPedalVisual(device, connected, artwork.ModelLabel);
        }

        var overlays = new Grid { Margin = new Thickness(36, 24, 36, 30) };
        canvas.Children.Add(overlays);
        for (var index = 0; index < device.SwitchCount; index++)
            overlays.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = device.SwitchCount == 3
                    ? new GridLength(index == 1 ? 52 : 24, GridUnitType.Star)
                    : new GridLength(1, GridUnitType.Star)
            });
        for (var index = 0; index < device.SwitchCount; index++)
        {
            var tile = CreateSwitchOverlay(device, index, connected);
            Grid.SetColumn(tile, index);
            overlays.Children.Add(tile);
        }

        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxHeight = UsesTabbedPedalPresentation()
                ? 485
                : ShouldUseSideBySide() ? 325 : 440,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = canvas
        };
    }

    private FrameworkElement CreateSubCompactPedalVisual(PedalDeviceProfile device, bool connected)
    {
        var artwork = _pedalRegistry.ResolveArtwork(device.ArtworkKey, GetDeviceInfo(device));
        var canvas = new Grid
        {
            Width = 760, Height = 485,
            Opacity = connected ? 1 : 0.58,
            Background = artwork is null ? (Brush)FindResource("SurfaceAltBrush") : Brushes.Transparent
        };
        if (artwork is not null && !string.IsNullOrWhiteSpace(artwork.ImagePath))
        {
            try
            {
                var bitmap = LoadArtworkBitmap(artwork.ImagePath);
                bitmap.Freeze();
                canvas.Children.Add(new Image
                {
                    Source = bitmap, Stretch = Stretch.Uniform, SnapsToDevicePixels = true
                });
            }
            catch
            {
                canvas.Background = (Brush)FindResource("SurfaceAltBrush");
            }
        }

        var overlays = new Grid { Margin = new Thickness(36, 24, 36, 30) };
        canvas.Children.Add(overlays);
        for (var index = 0; index < device.SwitchCount; index++)
            overlays.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = device.SwitchCount == 3
                    ? new GridLength(index == 1 ? 52 : 24, GridUnitType.Star)
                    : new GridLength(1, GridUnitType.Star)
            });
        for (var index = 0; index < device.SwitchCount; index++)
        {
            var tile = new Border
            {
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(18),
                Margin = new Thickness(4), Opacity = connected ? 1 : 0.82
            };
            _switchTiles[(device.DeviceKey, index)] = tile;
            Grid.SetColumn(tile, index);
            overlays.Children.Add(tile);
        }

        return new Viewbox
        {
            Width = 190, Height = 121.25,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = canvas
        };
    }

    private FrameworkElement CreateGenericPedalVisual(PedalDeviceProfile device, bool connected, string? modelLabel = null)
    {
        var columns = Math.Min(6, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(device.SwitchCount))));
        var rows = (int)Math.Ceiling(device.SwitchCount / (double)columns);
        var panel = new System.Windows.Controls.Primitives.UniformGrid
        {
            Columns = columns,
            Rows = rows,
            Width = 760,
            Height = Math.Clamp(rows * 185, 260, 740),
            Background = (Brush)FindResource("SurfaceAltBrush")
        };
        for (var index = 0; index < device.SwitchCount; index++)
        {
            var tile = CreateSwitchOverlay(device, index, connected);
            tile.Background = (Brush)FindResource("SurfaceBrush");
            tile.BorderBrush = (Brush)FindResource("BorderBrush");
            tile.BorderThickness = new Thickness(2);
            tile.Tag = "GenericSwitch";
            tile.ToolTip = $"Switch {index + 1}";
            panel.Children.Add(tile);
        }
        var shell = new Grid { Width = 760 };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition());
        shell.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(modelLabel) ? $"Unknown pedal · VID_{device.VendorId:X4} PID_{device.ProductId:X4}" : modelLabel,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 10),
            FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("MutedTextBrush")
        });
        Grid.SetRow(panel, 1);
        shell.Children.Add(panel);
        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxHeight = UsesTabbedPedalPresentation()
                ? 485
                : ShouldUseSideBySide() ? 325 : 440,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = shell
        };
    }

    private PedalDeviceInfo GetDeviceInfo(PedalDeviceProfile device) =>
        _connected.TryGetValue(device.DeviceKey, out var connected)
            ? connected
            : new PedalDeviceInfo(device.DeviceKey, device.DisplayName, device.VendorId, device.ProductId,
                string.Empty, string.Empty, device.SwitchCount);

    private void ShowArtworkPicker(PedalDeviceProfile device, PedalDeviceInfo info, bool ambiguous)
    {
        var options = _pedalRegistry.GetArtworkOptions();
        var picker = new PedalArtworkPickerWindow(device.DisplayName, info.VidPid, options,
            device.ArtworkKey, ambiguous) { Owner = this };
        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedArtworkKey)) return;
        device.ArtworkKey = picker.SelectedArtworkKey;
        RefreshDevices();
        ScheduleSave();
        SetStatus($"Updated picture for {device.DisplayName}");
    }

    private void QueueArtworkPicker(PedalDeviceProfile device, PedalDeviceInfo info)
    {
        _pendingArtworkPickers.Enqueue((device, info));
        _ = Dispatcher.BeginInvoke(new Action(ShowNextArtworkPicker));
    }

    private void ShowNextArtworkPicker()
    {
        if (_artworkPickerOpen || _pendingArtworkPickers.Count == 0) return;
        var pending = _pendingArtworkPickers.Dequeue();
        _artworkPickerOpen = true;
        try
        {
            ShowArtworkPicker(pending.Profile, pending.Info, true);
        }
        finally
        {
            _artworkPickerOpen = false;
            if (_pendingArtworkPickers.Count > 0)
                _ = Dispatcher.BeginInvoke(new Action(ShowNextArtworkPicker));
        }
    }

    private static BitmapImage LoadArtworkBitmap(string path)
    {
        if (path.StartsWith("/", StringComparison.Ordinal))
        {
            var resource = Application.GetResourceStream(new Uri(path, UriKind.Relative)) ??
                throw new FileNotFoundException($"Embedded pedal artwork was not found: {path}");
            using var stream = resource.Stream;
            var embedded = new BitmapImage();
            embedded.BeginInit();
            embedded.CacheOption = BitmapCacheOption.OnLoad;
            embedded.StreamSource = stream;
            embedded.EndInit();
            return embedded;
        }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        return bitmap;
    }

    private Border CreateSwitchOverlay(PedalDeviceProfile device, int switchIndex, bool connected)
    {
        var bankIndex = GetEffectiveBankIndex(device);
        var binding = device.Banks[bankIndex].Bindings[switchIndex];
        var tile = new Border
        {
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(18),
            Margin = new Thickness(4), Opacity = connected ? 1 : 0.82
        };
        _switchTiles[(device.DeviceKey, switchIndex)] = tile;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tile.Child = grid;
        var labelPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 13, 20, 28)),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Bottom
        };
        var description = new StackPanel();
        labelPanel.Child = description;
        description.Children.Add(new TextBlock
        {
            Text = binding.DisplayName, FontSize = 19, Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        });
        description.Children.Add(new TextBlock
        {
            Text = binding.Summary, Foreground = new SolidColorBrush(Color.FromRgb(183, 207, 226)),
            Margin = new Thickness(0, 3, 0, 6), TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, FontSize = 14, MaxHeight = 40
        });
        var edit = new Button
        {
            Content = "Edit assignment", HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(11, 5, 11, 5), FontSize = 13
        };
        edit.Click += (_, _) => EditBinding(device, switchIndex, bankIndex);
        description.Children.Add(edit);
        Grid.SetRow(labelPanel, 1);
        grid.Children.Add(labelPanel);
        return tile;
    }

    private void EditBinding(PedalDeviceProfile device, int switchIndex, int? bankIndex = null)
    {
        var targetBankIndex = Math.Clamp(bankIndex ?? device.ActiveBankIndex, 0, AppProfile.MaxBanks - 1);
        var binding = device.Banks[targetBankIndex].Bindings[switchIndex];
        var editor = new MacroEditorWindow(binding)
        {
            Owner = this, Title = $"Bank {targetBankIndex + 1} · {device.DisplayName} · Pedal {switchIndex + 1}"
        };
        if (editor.ShowDialog() == true)
        {
            device.Banks[targetBankIndex].Bindings[switchIndex] = editor.Result;
            RefreshDevices();
            ScheduleSave();
        }
    }

    private async void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Tippy profile (*.tippy.json)|*.tippy.json|JSON profile (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ReleaseAllInputs();
            _profile = await _profileStore.LoadAsync(dialog.FileName);
            _activeApplicationProfileId = null;
            _bankResolver.Clear();
            ThemeService.Apply(_profile.Theme);
            SetLayoutSelector();
            _hid.ConfigureLearnedDevices(_profile.LearnedPedals);
            SyncRawInputDevices();
            _macroPlayer.ConfigureSafety(_profile.Safety);
            _macroPlayer.ConfigureMidi(_profile.Midi);
            _gestureEngine.ConfigureMaximumRepeatDuration(TimeSpan.FromSeconds(_profile.Safety.MaximumRepeatSeconds));
            _patternEngine.Configure(_profile.PedalPatterns);
            BuildBankButtons(); RefreshDevices(); UpdateHeader(); RegisterHotkeys();
            await _hid.ScanAsync();
            await _profileStore.SaveDefaultAsync(_profile);
            SetStatus($"Loaded {System.IO.Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not open profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Tippy profile (*.tippy.json)|*.tippy.json", FileName = $"{SafeFileName(_profile.Name)}.tippy.json",
            AddExtension = true, DefaultExt = ".tippy.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _profileStore.SaveAsync(dialog.FileName, _profile);
            SetStatus($"Saved {System.IO.Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(_profile.BankHotkey, _profile.StartMinimized, _gamepad,
            _profile.Safety, _profile.Overlay, _profile.Variables) { Owner = this };
        if (settings.ShowDialog() == true)
        {
            _profile.BankHotkey = settings.BankHotkey;
            _profile.StartMinimized = settings.StartMinimized;
            _profile.Safety = settings.Safety;
            _profile.Overlay = settings.Overlay;
            _profile.Variables = settings.Variables.ToList();
            _macroPlayer.ConfigureSafety(_profile.Safety);
            _gestureEngine.ConfigureMaximumRepeatDuration(TimeSpan.FromSeconds(_profile.Safety.MaximumRepeatSeconds));
            RegisterHotkeys(); UpdateHeader(); ScheduleSave();
        }
    }

    private void ApplicationProfiles_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ApplicationProfilesWindow(_profile.ApplicationProfiles, _profile.Devices)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true) return;
        _profile.ApplicationProfiles = editor.Result.Select(profile => profile.Clone()).ToList();
        foreach (var profile in _profile.ApplicationProfiles) profile.Normalize();
        _activeApplicationProfileId = null;
        RefreshDevices();
        UpdateHeader();
        ScheduleSave();
        SetStatus($"Saved {_profile.ApplicationProfiles.Count} foreground application profile{(_profile.ApplicationProfiles.Count == 1 ? string.Empty : "s")}");
    }

    private void Tools_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateToolsItem("Foot combinations & sequences", OpenFootPatterns));
        menu.Items.Add(CreateToolsItem("Live pedal diagnostics", OpenDiagnostics));
        menu.Items.Add(CreateToolsItem("MIDI output setup", OpenMidiSetup));
        menu.Items.Add(CreateToolsItem("Sub-compact pedal view", () => SetSubCompactMode(true)));
        var rehearsal = new MenuItem { Header = "Rehearsal mode — preview without output", IsCheckable = true, IsChecked = _rehearsalMode };
        rehearsal.Click += (_, _) => SetRehearsalMode(rehearsal.IsChecked);
        menu.Items.Add(rehearsal);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateToolsItem("Profile backups & portable mode", OpenStorageTools));
        menu.Items.Add(CreateToolsItem("Install pedal support pack", InstallPedalSupportPack));
        menu.Items.Add(CreateToolsItem("Learn keyboard-style pedal", LearnRawInputPedal));
        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private static MenuItem CreateToolsItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OpenFootPatterns()
    {
        var wasRehearsing = _rehearsalMode;
        if (!wasRehearsing) SetRehearsalMode(true);
        var editor = new FootPatternsWindow(_profile.PedalPatterns, _profile.Devices, _pedalActivity) { Owner = this };
        bool accepted;
        try { accepted = editor.ShowDialog() == true; }
        finally { if (!wasRehearsing) SetRehearsalMode(false); }
        if (!accepted) return;
        _profile.PedalPatterns = editor.Result.Select(pattern => pattern.Clone()).ToList();
        _patternEngine.Configure(_profile.PedalPatterns);
        ScheduleSave();
        SetStatus($"Saved {_profile.PedalPatterns.Count} foot pattern{(_profile.PedalPatterns.Count == 1 ? string.Empty : "s")}");
    }

    private void OpenDiagnostics()
    {
        if (_diagnosticsWindow is { IsLoaded: true })
        {
            _diagnosticsWindow.Activate();
            return;
        }
        _diagnosticsWindow = new PedalDiagnosticsWindow { Owner = this };
        _diagnosticsWindow.SetDevices(_connected.Values.ToArray());
        _diagnosticsWindow.Closed += (_, _) => _diagnosticsWindow = null;
        _diagnosticsWindow.Show();
    }

    private void OpenMidiSetup()
    {
        var setup = new MidiSetupWindow(_profile.Midi) { Owner = this };
        if (setup.ShowDialog() != true) return;
        _profile.Midi = setup.Result;
        _profile.Midi.Normalize();
        _macroPlayer.ConfigureMidi(_profile.Midi);
        ScheduleSave();
        SetStatus(string.IsNullOrWhiteSpace(_profile.Midi.PreferredOutputName)
            ? "MIDI macros will use the Windows default output"
            : $"MIDI macros will use {_profile.Midi.PreferredOutputName}");
    }

    private void ShowOverlay(string title, string context)
    {
        if (!_profile.Overlay.Enabled) return;
        _overlayWindow ??= new StatusOverlayWindow();
        _overlayWindow.ShowStatus(title, context, _profile.Overlay);
    }

    private void SetRehearsalMode(bool enabled)
    {
        if (_rehearsalMode == enabled) return;
        _macroPlayer.ReleaseAll();
        _gestureEngine.ReleaseAll();
        _rehearsalMode = enabled;
        SetStatus(enabled
            ? "Rehearsal mode active · pedal actions are previewed but not sent"
            : "Rehearsal mode ended · pedal output restored");
        ShowOverlay(enabled ? "Rehearsal mode" : "Live output", enabled ? "No keyboard, mouse, MIDI, OSC, or gamepad output" : "Pedal actions are active");
    }

    private void OpenStorageTools()
    {
        var tools = new StorageToolsWindow(_profileStore, _profile) { Owner = this };
        if (tools.ShowDialog() != true || tools.RestoredProfile is null) return;
        ReleaseAllInputs();
        _profile = tools.RestoredProfile;
        _profile.Normalize();
        _activeApplicationProfileId = null;
        _bankResolver.Clear();
        _macroPlayer.ConfigureSafety(_profile.Safety);
        _macroPlayer.ConfigureMidi(_profile.Midi);
        _gestureEngine.ConfigureMaximumRepeatDuration(TimeSpan.FromSeconds(_profile.Safety.MaximumRepeatSeconds));
        _patternEngine.Configure(_profile.PedalPatterns);
        ThemeService.Apply(_profile.Theme);
        SetLayoutSelector();
        _hid.ConfigureLearnedDevices(_profile.LearnedPedals);
        SyncRawInputDevices();
        BuildBankButtons();
        RefreshDevices();
        UpdateHeader();
        RegisterHotkeys();
        SetStatus("Restored profile backup");
    }

    private async void InstallPedalSupportPack()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Install checksum-verified Tippy pedal support pack",
            Filter = "Tippy pedal packs (*.tippy-pedal-pack.zip)|*.tippy-pedal-pack.zip|ZIP archives (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var result = await new DeviceSupportPackService().InstallAsync(dialog.FileName);
            _pedalRegistry.Reload();
            RefreshDevices();
            SetStatus($"Installed pedal pack {result.PackId} {result.Version} · {result.FileCount} verified files");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Pedal support pack", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LearnRawInputPedal()
    {
        var wasRehearsing = _rehearsalMode;
        if (!wasRehearsing) SetRehearsalMode(true);
        var learner = new RawInputLearnWindow(_rawInput) { Owner = this };
        bool accepted;
        try { accepted = learner.ShowDialog() == true && learner.Result is not null; }
        finally { if (!wasRehearsing) SetRehearsalMode(false); }
        if (!accepted || learner.Result is null) return;
        _profile.RawInputPedals.RemoveAll(definition =>
            definition.DevicePath.Equals(learner.Result.DevicePath, StringComparison.OrdinalIgnoreCase));
        _profile.RawInputPedals.Add(learner.Result);
        SyncRawInputDevices();
        ScheduleSave();
        SetStatus($"Learned keyboard-style pedal · {learner.Result.DisplayName}");
    }

    private void InitializePlatformInput()
    {
        RegisterHotkeys();
        try { _rawInput.Initialize(this); }
        catch (Exception exception) { SetStatus($"Raw Input unavailable: {exception.Message}", true); }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyLayoutChrome();
        _windowPlacement.Restore(this, _profile.WindowPlacement);
        _windowStateBeforeTray = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        InitializePlatformInput();
    }

    private void SyncRawInputDevices()
    {
        if (!_loaded) return;
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in _profile.RawInputPedals)
        {
            if (!_rawInput.Devices.Any(device =>
                    device.DevicePath.Equals(definition.DevicePath, StringComparison.OrdinalIgnoreCase))) continue;
            var info = CreateRawInputInfo(definition);
            active.Add(info.DeviceKey);
            if (!_connected.ContainsKey(info.DeviceKey))
                Hid_ConnectionChanged(this, new PedalConnectionEventArgs(info, true));
        }
        foreach (var info in _connected.Values.Where(info => info.DecoderName == "Windows Raw Input" &&
                     !active.Contains(info.DeviceKey)).ToArray())
            Hid_ConnectionChanged(this, new PedalConnectionEventArgs(info, false));
    }

    private void RawInput_KeyChanged(object? sender, RawInputKeyEvent e)
    {
        var definition = _profile.RawInputPedals.FirstOrDefault(item =>
            item.DevicePath.Equals(e.DevicePath, StringComparison.OrdinalIgnoreCase));
        var mapping = definition?.Switches.FirstOrDefault(item => item.VirtualKey == e.VirtualKey);
        if (definition is null || mapping is null) return;
        var info = CreateRawInputInfo(definition);
        Hid_StateChanged(this, new PedalStateEventArgs(info, mapping.SwitchIndex, e.IsPressed,
            BitConverter.GetBytes(e.VirtualKey), false, e.Timestamp));
    }

    private static PedalDeviceInfo CreateRawInputInfo(RawInputPedalDefinition definition)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(definition.DevicePath)))[..12];
        var vidMatch = System.Text.RegularExpressions.Regex.Match(definition.DevicePath, "VID_([0-9A-F]{4})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var pidMatch = System.Text.RegularExpressions.Regex.Match(definition.DevicePath, "PID_([0-9A-F]{4})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var vid = vidMatch.Success ? Convert.ToInt32(vidMatch.Groups[1].Value, 16) : 0;
        var pid = pidMatch.Success ? Convert.ToInt32(pidMatch.Groups[1].Value, 16) : 0;
        return new PedalDeviceInfo($"RAW:{hash}", definition.DisplayName, vid, pid,
            definition.DevicePath, "Windows Raw Input", Math.Max(1, definition.Switches.Count));
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void InitializeTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show Tippy", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("Release all held inputs", null, (_, _) => Dispatcher.Invoke(ReleaseAllInputs));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Tippy — Foot Control Macros",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty),
            ContextMenuStrip = menu,
            Visible = false
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
                Dispatcher.Invoke(RestoreFromTray);
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
    }

    private void ReleaseAllInputs()
    {
        _macroPlayer.ReleaseAll();
        ClearTrackedActions("Stopped Tippy output and released all held keyboard, mouse, and gamepad inputs");
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.SessionLogoff:
                SuspendInput(InputSuspensionReason.SessionLocked);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.SessionLogon:
                ResumeInput(InputSuspensionReason.SessionLocked);
                break;
            case SessionSwitchReason.RemoteDisconnect:
                SuspendInput(InputSuspensionReason.RemoteDisconnected);
                break;
            case SessionSwitchReason.RemoteConnect:
                ResumeInput(InputSuspensionReason.RemoteDisconnected);
                break;
            case SessionSwitchReason.ConsoleDisconnect:
                SuspendInput(InputSuspensionReason.ConsoleDisconnected);
                break;
            case SessionSwitchReason.ConsoleConnect:
                ResumeInput(InputSuspensionReason.ConsoleDisconnected);
                break;
        }
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            SuspendInput(InputSuspensionReason.Power);
        }
        else if (e.Mode == PowerModes.Resume)
        {
            ResumeInput(InputSuspensionReason.Power);
        }
    }

    private void SuspendInput(InputSuspensionReason reason)
    {
        int generation;
        lock (_inputStateGate)
        {
            _inputSuspensionReasons |= reason;
            _inputSuspended = true;
            generation = ++_inputStateGeneration;
            _macroPlayer.Suspend();
            _gestureEngine.ReleaseAll();
            _patternEngine.Clear();
            _bankResolver.Clear();
        }
        QueueSuspendedUiUpdate(generation);
    }

    private void ResumeInput(InputSuspensionReason reason)
    {
        int generation;
        lock (_inputStateGate)
        {
            _inputSuspensionReasons &= ~reason;
            generation = ++_inputStateGeneration;
            if (_inputSuspensionReasons != InputSuspensionReason.None)
            {
                _inputSuspended = true;
                QueueSuspendedUiUpdate(generation);
                return;
            }
        }

        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
        {
            lock (_inputStateGate)
            {
                if (generation != _inputStateGeneration ||
                    _inputSuspensionReasons != InputSuspensionReason.None)
                {
                    return;
                }
                _macroPlayer.Resume();
                _inputSuspended = false;
            }
            _gestureEngine.ReleaseAll();
            _patternEngine.Clear();
            _pressedSwitches.Clear();
            RefreshDevices();
            _ = _hid.ScanAsync();
            SetStatus("USB foot-control input resumed");
        }));
    }

    private void QueueSuspendedUiUpdate(int generation)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
        {
            lock (_inputStateGate)
            {
                if (generation != _inputStateGeneration ||
                    _inputSuspensionReasons == InputSuspensionReason.None)
                {
                    return;
                }
                var sessionReasons = InputSuspensionReason.SessionLocked |
                                     InputSuspensionReason.RemoteDisconnected |
                                     InputSuspensionReason.ConsoleDisconnected;
                var status = (_inputSuspensionReasons & sessionReasons) != 0
                    ? "Input paused while Windows is locked or disconnected"
                    : "Input paused while Windows is suspended";
                ClearTrackedActions(status);
            }
        }));
    }

    private void ClearTrackedActions(string status)
    {
        _gestureEngine.ReleaseAll();
        _patternEngine.Clear();
        _bankResolver.Clear();
        SetStatus(status);
        if (_loaded) RefreshDevices();
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void CompactMode_Click(object sender, RoutedEventArgs e) => SetCompactMode(true);

    private void SubCompactMode_Click(object sender, RoutedEventArgs e) => SetSubCompactMode(true);

    private void ExitCompactMode_Click(object sender, RoutedEventArgs e) => SetCompactMode(false);

    private void ExitSubCompactMode_Click(object sender, RoutedEventArgs e) => SetFullView();

    private void SubCompactSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_profile.IsSubCompactMode || e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is DependencyObject source && IsInsideSubCompactDot(source)) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private static bool IsInsideSubCompactDot(DependencyObject source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Border { Tag: "SubCompactDot" }) return true;
        }
        return false;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var condensed = _profile.IsCompactMode || _profile.IsSubCompactMode;
        if (e.Key == Key.F11 || (condensed && e.Key == Key.Escape))
        {
            if (condensed) SetFullView();
            else SetCompactMode(true);
            e.Handled = true;
        }
    }

    private void SetCompactMode(bool enabled)
    {
        if (!enabled)
        {
            SetFullView();
            return;
        }
        if (_profile.IsCompactMode && !_profile.IsSubCompactMode) return;
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        _profile.IsCompactMode = true;
        _profile.IsSubCompactMode = false;
        _lastOptimizationKey = string.Empty;
        RefreshDevices(true);
        ScheduleSave();
        SetStatus("Compact pedal view active · press Esc or F11 for the full interface");
    }

    private void SetSubCompactMode(bool enabled)
    {
        if (!enabled)
        {
            SetFullView();
            return;
        }
        if (_profile.IsSubCompactMode) return;
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        _profile.IsCompactMode = false;
        _profile.IsSubCompactMode = true;
        _lastOptimizationKey = string.Empty;
        RefreshDevices(true);
        ScheduleSave();
        SetStatus("Sub-compact pedal view active · press Esc or F11 for the full interface");
    }

    private void SetFullView()
    {
        if (!_profile.IsCompactMode && !_profile.IsSubCompactMode) return;
        _profile.IsCompactMode = false;
        _profile.IsSubCompactMode = false;
        _lastOptimizationKey = string.Empty;
        RefreshDevices(true);
        ScheduleSave();
        SetStatus("Full interface restored");
    }

    private void HideToTray()
    {
        if (_trayIcon is null) return;
        if (WindowState != WindowState.Minimized)
            _windowStateBeforeTray = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        RememberWindowPlacement();
        _trayIcon.Visible = true;
        Hide();
        if (!_trayTipShown)
        {
            _trayIcon.ShowBalloonTip(2500, "Tippy is still running",
                "Double-click the tray icon to restore Tippy. Your pedals remain active.",
                System.Windows.Forms.ToolTipIcon.Info);
            _trayTipShown = true;
        }
    }

    private void RestoreFromTray()
    {
        if (_trayIcon is not null) _trayIcon.Visible = false;
        Show();
        WindowState = _windowStateBeforeTray;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void DeviceLayoutBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _updatingLayoutSelection) return;
        if (!Enum.TryParse<PedalLayoutMode>((DeviceLayoutBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var mode))
            mode = PedalLayoutMode.Auto;
        _profile.PedalLayout = mode;
        _lastOptimizationKey = string.Empty;
        RefreshDevices(true);
        ScheduleSave();
        SetStatus($"Pedal layout: {(mode == PedalLayoutMode.SideBySide ? "side by side" : mode.ToString().ToLowerInvariant())}");
    }

    private void TileColumnsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _updatingLayoutSelection) return;
        _profile.TileColumns = int.TryParse((TileColumnsBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var columns)
            ? Math.Clamp(columns, 0, 6)
            : 0;
        _lastOptimizationKey = string.Empty;
        RefreshDevices(true);
        ScheduleSave();
        SetStatus(_profile.TileColumns == 0
            ? "Tile columns: automatic"
            : $"Tile columns: {_profile.TileColumns}");
    }

    private void SetLayoutSelector()
    {
        _updatingLayoutSelection = true;
        DeviceLayoutBox.SelectedIndex = _profile.PedalLayout switch
        {
            PedalLayoutMode.Stacked => 1,
            PedalLayoutMode.SideBySide => 2,
            PedalLayoutMode.Tiled => 3,
            PedalLayoutMode.Tabbed => 4,
            _ => 0
        };
        TileColumnsBox.SelectedIndex = Math.Clamp(_profile.TileColumns, 0, 6);
        TileColumnsPanel.Visibility = _profile.PedalLayout == PedalLayoutMode.Tiled
            ? Visibility.Visible
            : Visibility.Collapsed;
        _updatingLayoutSelection = false;
    }

    private bool ShouldUseSideBySide()
    {
        if (_profile.PedalLayout == PedalLayoutMode.Stacked) return false;
        if (_profile.PedalLayout == PedalLayoutMode.SideBySide) return true;
        if (_profile.PedalLayout == PedalLayoutMode.Tiled)
            return GetGridColumnCount(_profile.Devices.Count) > 1;
        if (_profile.PedalLayout == PedalLayoutMode.Tabbed) return false;
        return _profile.Devices.Count > 1 && ActualWidth >= 1220;
    }

    private int GetGridColumnCount(int count)
    {
        count = Math.Max(1, count);
        return _profile.PedalLayout switch
        {
            PedalLayoutMode.Stacked => 1,
            PedalLayoutMode.SideBySide => count,
            PedalLayoutMode.Tabbed => 1,
            PedalLayoutMode.Tiled when _profile.TileColumns > 0 => Math.Min(_profile.TileColumns, count),
            PedalLayoutMode.Tiled => Math.Min(count, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)))),
            _ => count > 1 && ActualWidth >= 1220 ? count : 1
        };
    }

    private void TabbedDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_buildingTabbedLayout || sender is not TabControl tabs ||
            tabs.SelectedItem is not TabItem selected || selected.Tag is not string deviceKey)
            return;
        _profile.SelectedTabbedDeviceKey = deviceKey;
        ScheduleSave();
        var device = _profile.Devices.FirstOrDefault(item =>
            item.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
        if (device is not null) SetStatus($"Showing {device.DisplayName}");
    }

    private void ApplyLayoutChrome()
    {
        var compact = _profile.IsCompactMode;
        var subCompact = _profile.IsSubCompactMode;
        MinWidth = subCompact ? 210 : compact ? 840 : 920;
        MinHeight = subCompact ? 180 : compact ? 700 : 650;
        WindowStyle = subCompact ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = subCompact ? ResizeMode.NoResize : ResizeMode.CanResize;
        SubCompactSurface.Visibility = subCompact ? Visibility.Visible : Visibility.Collapsed;
        CompactModeBar.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        HeaderBorder.Visibility = compact || subCompact ? Visibility.Collapsed : Visibility.Visible;
        AllPedalsBorder.Visibility = compact || subCompact ? Visibility.Collapsed : Visibility.Visible;
        DevicesScrollViewer.Visibility = subCompact ? Visibility.Collapsed : Visibility.Visible;
        DevicesToolbar.Visibility = compact || subCompact ? Visibility.Collapsed : Visibility.Visible;
        StatusBorder.Visibility = compact || subCompact ? Visibility.Collapsed : Visibility.Visible;
        HeaderBorder.Padding = compact ? new Thickness(12, 6, 12, 6) : new Thickness(20, 8, 20, 8);
        BrandBadge.Padding = compact ? new Thickness(6, 2, 8, 3) : new Thickness(8, 3, 10, 4);
        HeaderMascot.Width = compact ? 52 : 66;
        HeaderMascot.Height = compact ? 68 : 86;
        HeaderMascot.Margin = compact ? new Thickness(0, -1, -3, -1) : new Thickness(0, -1, -4, -1);
        HeaderWordmark.Width = compact ? 90 : 112;
        HeaderWordmark.Height = compact ? 61 : 76;
        HeaderWordmark.Margin = new Thickness(0, 0, 0, -1);
        HeaderTagline.Visibility = Visibility.Visible;
        HeaderTagline.FontSize = compact ? 10 : 12;
        HeaderTagline.Margin = compact ? new Thickness(0, -4, 0, 0) : new Thickness(0, -5, 0, 0);
        AllPedalsBorder.Padding = compact ? new Thickness(12, 8, 12, 8) : new Thickness(24, 13, 24, 13);
        DevicesContent.Margin = compact ? new Thickness(12, 10, 12, 12) : new Thickness(24, 22, 24, 30);
        DevicesToolbar.Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 14);
        StatusBorder.Padding = compact ? new Thickness(10, 6, 10, 6) : new Thickness(16, 9, 16, 9);
        foreach (var button in _bankButtons)
            button.Padding = compact ? new Thickness(12, 6, 12, 6) : new Thickness(18, 9, 18, 9);
        TileColumnsPanel.Visibility = !compact && !subCompact && _profile.PedalLayout == PedalLayoutMode.Tiled
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool UsesTabbedPedalPresentation() =>
        _profile.IsCompactMode || _profile.PedalLayout == PedalLayoutMode.Tabbed;

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_loaded) return;
        RememberWindowPlacement();
        if (_profile.PedalLayout != PedalLayoutMode.Auto) return;
        var next = ShouldUseSideBySide();
        if (_lastAutoSideBySide.HasValue && next != _lastAutoSideBySide.Value)
            RefreshDevices();
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (_loaded) RememberWindowPlacement();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (!_loaded) return;
        if (WindowState != WindowState.Minimized)
            _windowStateBeforeTray = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        RememberWindowPlacement();
    }

    private void RememberWindowPlacement()
    {
        if (!_loaded) return;
        _windowPlacement.Capture(this, _profile.WindowPlacement);
        ScheduleSave();
    }

    private void OptimizeWindowForPedals()
    {
        if (WindowState != WindowState.Normal) return;
        var count = Math.Max(1, _profile.Devices.Count);
        var optimizationKey = GetWindowOptimizationKey();
        if (optimizationKey == _lastOptimizationKey) return;
        _lastOptimizationKey = optimizationKey;
        if (_profile.IsSubCompactMode)
        {
            _windowPlacement.ResizeWithinCurrentMonitor(this, 210, 180, 8);
            RememberWindowPlacement();
            return;
        }
        if (_profile.IsCompactMode)
        {
            _windowPlacement.ResizeWithinCurrentMonitor(this, 840, 720, 12);
            RememberWindowPlacement();
            return;
        }
        if (_profile.PedalLayout == PedalLayoutMode.Tabbed)
        {
            _windowPlacement.ResizeWithinCurrentMonitor(this, 1050, 900, 16);
            RememberWindowPlacement();
            return;
        }

        var columns = GetGridColumnCount(count);
        var rows = (int)Math.Ceiling(count / (double)columns);
        var sideBySide = _profile.PedalLayout switch
        {
            PedalLayoutMode.Stacked => false,
            PedalLayoutMode.SideBySide => true,
            PedalLayoutMode.Tiled => columns > 1,
            _ => count > 1 && ActualWidth >= 1220
        };
        var desiredWidth = _profile.PedalLayout == PedalLayoutMode.Tiled
            ? columns switch { 1 => 1050, 2 => 1510, _ => 1680 }
            : count switch
        {
            <= 1 => 1050,
            2 when sideBySide => 1510,
            2 => 1200,
            _ => sideBySide ? 1680 : 1240
        };
        var desiredHeight = _profile.PedalLayout == PedalLayoutMode.Tiled
            ? rows switch { <= 1 => 780, 2 => 980, _ => 1040 }
            : count switch
        {
            <= 1 => 735,
            2 when sideBySide => 770,
            2 => 980,
            _ => sideBySide ? 830 : 1040
        };
        _windowPlacement.ResizeWithinCurrentMonitor(this, desiredWidth, desiredHeight, 16);
        RememberWindowPlacement();
    }

    private string GetWindowOptimizationKey() =>
        $"{Math.Max(1, _profile.Devices.Count)}:{_profile.PedalLayout}:{_profile.TileColumns}:{_profile.IsCompactMode}:{_profile.IsSubCompactMode}";

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _profile.Theme = _profile.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ThemeService.Apply(_profile.Theme);
        UpdateHeader(); UpdateBankButtons(); RefreshDevices(); ScheduleSave();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await _hid.ScanAsync();
        SetStatus($"Scan complete · {_connected.Count} connected");
    }

    private void LearnPedal_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new LearnPedalWindow { Owner = this };
        if (wizard.ShowDialog() != true || wizard.Result is null) return;
        _profile.LearnedPedals.RemoveAll(item =>
            item.Id.Equals(wizard.Result.Id, StringComparison.OrdinalIgnoreCase) ||
            item.MatchesHardwareIdentity(wizard.Result));
        _profile.LearnedPedals.Add(wizard.Result);
        _hid.AddLearnedDevice(wizard.Result);
        ScheduleSave();
        SetStatus($"Learned {wizard.Result.Name}; scanning for it now");
    }

    private void RegisterBankHotkey()
    {
        if (!_loaded || new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        if (!_hotkey.Register(this, _profile.BankHotkey,
                () => Dispatcher.Invoke(() =>
                {
                    var current = _profile.Devices.Count == 0 ? _profile.ActiveBankIndex : _profile.Devices.Max(device => device.ActiveBankIndex);
                    SwitchAllBanks((current + 1) % AppProfile.MaxBanks);
                }), out var error))
        {
            SetStatus(error ?? "Could not register bank hotkey", true);
        }
    }

    private void RegisterHotkeys()
    {
        RegisterBankHotkey();
        if (!_loaded || new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        if (!_emergencyHotkey.Register(this, _profile.Safety.EmergencyStopHotkey,
                () => Dispatcher.Invoke(ReleaseAllInputs), out var error))
            SetStatus(error ?? "Could not register emergency-stop hotkey", true);
    }

    private void UpdateHeader()
    {
        ThemeButton.Content = _profile.Theme == AppTheme.Dark ? "Light mode" : "Dark mode";
        var applicationProfile = GetActiveApplicationProfile();
        BankHintText.Text = applicationProfile is null
            ? $"{_profile.BankHotkey.Replace("+", " + ")} switches bank"
            : $"{applicationProfile.Name} profile · {_profile.BankHotkey.Replace("+", " + ")} switches bank";
    }

    private void SetStatus(string text, bool isError = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "MutedTextBrush");
    }

    private void ScheduleSave()
    {
        if (!_loaded) return;
        _saveDebounce?.Cancel();
        _saveDebounce?.Dispose();
        _saveDebounce = new CancellationTokenSource();
        var token = _saveDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                await _profileStore.SaveDefaultAsync(_profile);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Dispatcher.Invoke(() => SetStatus($"Autosave failed: {exception.Message}", true));
            }
        }, token);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        _windowPlacement.Capture(this, _profile.WindowPlacement);
        _saveDebounce?.Cancel();
        try { _profileStore.SaveDefaultAsync(_profile).GetAwaiter().GetResult(); } catch { }
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _overlayWindow?.Close();
        _hotkey.Dispose(); _emergencyHotkey.Dispose(); _rawInput.Dispose(); _gestureEngine.Dispose(); _hid.Dispose(); _macroPlayer.Dispose(); _gamepad.Dispose(); _saveDebounce?.Dispose();
    }

    private static string ShortDeviceKey(string key) => key.Length <= 12 ? key : key[^12..];
    private static string SafeFileName(string name) => string.Concat(name.Select(character =>
        System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
