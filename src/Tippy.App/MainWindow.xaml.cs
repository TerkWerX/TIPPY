using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Tippy.App.Models;
using Tippy.App.Services;
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
    private readonly VirtualGamepadService _gamepad = new();
    private readonly MacroPlayer _macroPlayer;
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly Dictionary<string, PedalDeviceInfo> _connected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string DeviceKey, int SwitchIndex), Border> _switchTiles = new();
    private readonly Dictionary<string, Border> _pedalTabHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string DeviceKey, int SwitchIndex)> _pressedSwitches = [];
    private readonly Dictionary<(string DeviceKey, int SwitchIndex), MacroDefinition> _activeHeldMacros = new();
    private readonly Dictionary<(string DeviceKey, int SwitchIndex), MacroDefinition> _pendingReleaseMacros = new();
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

    public MainWindow()
    {
        InitializeComponent();
        InitializeTrayIcon();
        _macroPlayer = new MacroPlayer(new WindowsInputService(), _gamepad);
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        SizeChanged += MainWindow_SizeChanged;
        SourceInitialized += (_, _) => RegisterBankHotkey();
        _hid.ConnectionChanged += Hid_ConnectionChanged;
        _hid.StateChanged += Hid_StateChanged;
        _hid.Diagnostic += (_, message) => Dispatcher.Invoke(() => SetStatus(message));
        _macroPlayer.PlaybackError += (_, error) => Dispatcher.Invoke(() => SetStatus(error, true));
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _profile = await _profileStore.LoadDefaultAsync();
        }
        catch (Exception exception)
        {
            _profile = new AppProfile();
            MessageBox.Show(this, $"The default profile could not be loaded. A fresh profile is active.\n\n{exception.Message}",
                "Tippy profile", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _profile.Normalize();
        ThemeService.Apply(_profile.Theme);
        SetLayoutSelector();
        _loaded = true;
        BuildBankButtons();
        RefreshDevices();
        UpdateHeader();
        RegisterBankHotkey();
        _hid.ConfigureLearnedDevices(_profile.LearnedPedals);
        _hid.Start();
        SetStatus("Listening for USB foot controls");
    }

    private void Hid_ConnectionChanged(object? sender, PedalConnectionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.IsConnected)
            {
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
            }
            else
            {
                ReleaseActionsForDevice(e.Device.DeviceKey);
                _connected.Remove(e.Device.DeviceKey);
            }
            RefreshDevices(true);
            SetStatus($"{_connected.Count} foot control{(_connected.Count == 1 ? string.Empty : "s")} connected");
        });
    }

    private void Hid_StateChanged(object? sender, PedalStateEventArgs e)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
        {
            if (_inputSuspended) return;
            RawReportText.Text = $"{e.Device.DisplayName}: {Convert.ToHexString(e.RawReport)}";
            var triggerId = $"{e.Device.DeviceKey}:{e.SwitchIndex}";
            var key = (e.Device.DeviceKey, e.SwitchIndex);
            if (e.IsPressed) _pressedSwitches.Add(key);
            else _pressedSwitches.Remove(key);
            if (_switchTiles.TryGetValue(key, out var tile))
            {
                var generic = Equals(tile.Tag, "GenericSwitch");
                tile.Background = e.IsPressed
                    ? new SolidColorBrush(Color.FromArgb(145, 70, 205, 255))
                    : generic ? (Brush)FindResource("SurfaceBrush") : Brushes.Transparent;
                tile.BorderBrush = e.IsPressed
                    ? new SolidColorBrush(Color.FromRgb(86, 220, 255))
                    : generic ? (Brush)FindResource("BorderBrush") : Brushes.Transparent;
                tile.BorderThickness = generic && !e.IsPressed ? new Thickness(2) : new Thickness(4);
                tile.RenderTransform = e.IsPressed ? new ScaleTransform(0.985, 0.985) : Transform.Identity;
                tile.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            if (_pedalTabHeaders.TryGetValue(e.Device.DeviceKey, out var tabHeader))
            {
                var anyPressed = _pressedSwitches.Any(item =>
                    item.DeviceKey.Equals(e.Device.DeviceKey, StringComparison.OrdinalIgnoreCase));
                tabHeader.Background = anyPressed
                    ? (Brush)FindResource("AccentSoftBrush")
                    : Brushes.Transparent;
            }

            if (!e.IsPressed)
            {
                if (_activeHeldMacros.Remove(key, out _))
                {
                    _macroPlayer.ReleaseHeld(triggerId);
                }
                if (_pendingReleaseMacros.Remove(key, out var releaseMacro))
                {
                    if (!e.IsSynthetic)
                    {
                        _macroPlayer.Handle(triggerId, releaseMacro, false);
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
            var binding = device.Banks[device.ActiveBankIndex].Bindings[e.SwitchIndex];
            switch (binding.Type)
            {
                case PedalBindingType.BankNext:
                    SwitchPedalBank(device, (device.ActiveBankIndex + 1) % AppProfile.MaxBanks);
                    break;
                case PedalBindingType.Macro:
                    var macro = binding.Macro.Clone();
                    if (macro.TriggerMode == MacroTriggerMode.WhileHeld)
                    {
                        _activeHeldMacros[key] = macro;
                    }
                    else if (macro.TriggerMode == MacroTriggerMode.ReleaseOnce)
                    {
                        _pendingReleaseMacros[key] = macro;
                    }
                    _macroPlayer.Handle(triggerId, macro, true);
                    SetStatus($"{e.Device.DisplayName} · Pedal {e.SwitchIndex + 1} · {binding.DisplayName}");
                    break;
            }
        }));
    }

    private void ReleaseActionsForDevice(string deviceKey)
    {
        _pressedSwitches.RemoveWhere(key =>
            key.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
        foreach (var key in _activeHeldMacros.Keys
                     .Where(key => key.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            _activeHeldMacros.Remove(key);
            _macroPlayer.ReleaseHeld($"{key.DeviceKey}:{key.SwitchIndex}");
        }
        foreach (var key in _pendingReleaseMacros.Keys
                     .Where(key => key.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            _pendingReleaseMacros.Remove(key);
        }
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
    }

    private void SwitchPedalBank(PedalDeviceProfile device, int bankIndex)
    {
        device.ActiveBankIndex = Math.Clamp(bankIndex, 0, AppProfile.MaxBanks - 1);
        UpdateBankButtons();
        RefreshDevices();
        ScheduleSave();
        SetStatus($"{device.DisplayName} · Bank {device.ActiveBankIndex + 1} active");
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
        EmptyState.Visibility = _profile.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var count = _profile.Devices.Count;

        ApplyLayoutChrome();
        if (_profile.PedalLayout == PedalLayoutMode.Tabbed && count > 0)
        {
            BuildTabbedDeviceLayout();
        }
        else
        {
            BuildGridDeviceLayout();
        }

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
        Grid.SetColumn(badge, 2);
        header.Children.Add(badge);
        stack.Children.Add(header);
        stack.Children.Add(CreateDeviceBankBar(device, compact));
        stack.Children.Add(CreatePedalVisual(device, connected));
        return shell;
    }

    private FrameworkElement CreateDeviceBankBar(PedalDeviceProfile device, bool compact)
    {
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
            Text = "BANK", Style = (Style)FindResource("SmallMutedText"), FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0)
        });
        for (var index = 0; index < AppProfile.MaxBanks; index++)
        {
            var bankIndex = index;
            var active = device.ActiveBankIndex == index;
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
        if (device.VendorId != Tippy.Core.Input.InfinityReportDecoder.VendorId ||
            device.ProductId != Tippy.Core.Input.InfinityReportDecoder.ProductId)
        {
            return CreateGenericPedalVisual(device, connected);
        }

        var altoEdge = device.DisplayName.Contains("Alto", StringComparison.OrdinalIgnoreCase);
        var canvas = new Grid
        {
            Width = 760,
            Height = 485,
            Opacity = connected ? 1 : 0.58
        };
        canvas.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri(altoEdge
                ? "/Tippy;component/Assets/Pedals/altoedge-in-ae-s-scale-matched.png"
                : "/Tippy;component/Assets/Pedals/infinity-in-usb-2.png", UriKind.RelativeOrAbsolute)),
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        });

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
            MaxHeight = _profile.PedalLayout == PedalLayoutMode.Tabbed
                ? 485
                : ShouldUseSideBySide() ? 325 : 440,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = canvas
        };
    }

    private FrameworkElement CreateGenericPedalVisual(PedalDeviceProfile device, bool connected)
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
        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxHeight = _profile.PedalLayout == PedalLayoutMode.Tabbed
                ? 485
                : ShouldUseSideBySide() ? 325 : 440,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = panel
        };
    }

    private Border CreateSwitchOverlay(PedalDeviceProfile device, int switchIndex, bool connected)
    {
        var binding = device.Banks[device.ActiveBankIndex].Bindings[switchIndex];
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
        edit.Click += (_, _) => EditBinding(device, switchIndex);
        description.Children.Add(edit);
        Grid.SetRow(labelPanel, 1);
        grid.Children.Add(labelPanel);
        return tile;
    }

    private void EditBinding(PedalDeviceProfile device, int switchIndex)
    {
        var binding = device.Banks[device.ActiveBankIndex].Bindings[switchIndex];
        var editor = new MacroEditorWindow(binding)
        {
            Owner = this, Title = $"Bank {device.ActiveBankIndex + 1} · {device.DisplayName} · Pedal {switchIndex + 1}"
        };
        if (editor.ShowDialog() == true)
        {
            device.Banks[device.ActiveBankIndex].Bindings[switchIndex] = editor.Result;
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
            _profile = await _profileStore.LoadAsync(dialog.FileName);
            ThemeService.Apply(_profile.Theme);
            SetLayoutSelector();
            _hid.ConfigureLearnedDevices(_profile.LearnedPedals);
            BuildBankButtons(); RefreshDevices(); UpdateHeader(); RegisterBankHotkey();
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
        var settings = new SettingsWindow(_profile.BankHotkey, _gamepad) { Owner = this };
        if (settings.ShowDialog() == true)
        {
            _profile.BankHotkey = settings.BankHotkey;
            RegisterBankHotkey(); UpdateHeader(); ScheduleSave();
        }
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
        ClearTrackedActions("Released all Tippy-held keyboard and gamepad inputs");
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
            _activeHeldMacros.Clear();
            _pendingReleaseMacros.Clear();
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
        _activeHeldMacros.Clear();
        _pendingReleaseMacros.Clear();
        SetStatus(status);
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void HideToTray()
    {
        if (_trayIcon is null) return;
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
        WindowState = WindowState.Normal;
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
        var compact = _profile.PedalLayout == PedalLayoutMode.Tabbed;
        MinWidth = compact ? 880 : 920;
        MinHeight = compact ? 760 : 650;
        HeaderBorder.Padding = compact ? new Thickness(12, 8, 12, 8) : new Thickness(24, 18, 24, 18);
        HeaderMascot.Width = compact ? 44 : 58;
        HeaderMascot.Height = compact ? 58 : 76;
        HeaderMascot.Margin = compact ? new Thickness(0, -8, 8, -8) : new Thickness(0, -14, 10, -14);
        HeaderWordmark.Width = compact ? 78 : 96;
        HeaderWordmark.Height = compact ? 52 : 66;
        HeaderTagline.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        AllPedalsBorder.Padding = compact ? new Thickness(12, 8, 12, 8) : new Thickness(24, 13, 24, 13);
        DevicesContent.Margin = compact ? new Thickness(12, 10, 12, 12) : new Thickness(24, 22, 24, 30);
        DevicesToolbar.Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 14);
        StatusBorder.Padding = compact ? new Thickness(10, 6, 10, 6) : new Thickness(16, 9, 16, 9);
        foreach (var button in _bankButtons)
            button.Padding = compact ? new Thickness(12, 6, 12, 6) : new Thickness(18, 9, 18, 9);
        TileColumnsPanel.Visibility = _profile.PedalLayout == PedalLayoutMode.Tiled
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_loaded || _profile.PedalLayout != PedalLayoutMode.Auto) return;
        var next = ShouldUseSideBySide();
        if (_lastAutoSideBySide.HasValue && next != _lastAutoSideBySide.Value)
            RefreshDevices();
    }

    private void OptimizeWindowForPedals()
    {
        if (WindowState != WindowState.Normal) return;
        var count = Math.Max(1, _profile.Devices.Count);
        var optimizationKey = $"{count}:{_profile.PedalLayout}:{_profile.TileColumns}";
        if (optimizationKey == _lastOptimizationKey) return;
        _lastOptimizationKey = optimizationKey;
        var work = SystemParameters.WorkArea;
        if (_profile.PedalLayout == PedalLayoutMode.Tabbed)
        {
            Width = Math.Min(880, work.Width - 32);
            Height = Math.Min(885, work.Height - 32);
            Left = Math.Max(work.Left + 16, Math.Min(Left, work.Right - Width - 16));
            Top = Math.Max(work.Top + 16, Math.Min(Top, work.Bottom - Height - 16));
            return;
        }

        var columns = GetGridColumnCount(count);
        var rows = (int)Math.Ceiling(count / (double)columns);
        var sideBySide = _profile.PedalLayout switch
        {
            PedalLayoutMode.Stacked => false,
            PedalLayoutMode.SideBySide => true,
            PedalLayoutMode.Tiled => columns > 1,
            _ => count > 1 && work.Width >= 1320
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
        Width = Math.Min(desiredWidth, work.Width - 32);
        Height = Math.Min(desiredHeight, work.Height - 32);
        Left = Math.Max(work.Left + 16, Math.Min(Left, work.Right - Width - 16));
        Top = Math.Max(work.Top + 16, Math.Min(Top, work.Bottom - Height - 16));
    }

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

    private void UpdateHeader()
    {
        ThemeButton.Content = _profile.Theme == AppTheme.Dark ? "Light mode" : "Dark mode";
        BankHintText.Text = $"{_profile.BankHotkey.Replace("+", " + ")} switches bank";
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
        _saveDebounce?.Cancel();
        try { _profileStore.SaveDefaultAsync(_profile).GetAwaiter().GetResult(); } catch { }
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _hotkey.Dispose(); _hid.Dispose(); _macroPlayer.Dispose(); _gamepad.Dispose(); _saveDebounce?.Dispose();
    }

    private static string ShortDeviceKey(string key) => key.Length <= 12 ? key : key[^12..];
    private static string SafeFileName(string name) => string.Concat(name.Select(character =>
        System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
