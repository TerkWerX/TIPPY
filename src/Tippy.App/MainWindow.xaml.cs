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
    private readonly ProfileStore _profileStore = new();
    private readonly PedalHidService _hid = new();
    private readonly VirtualGamepadService _gamepad = new();
    private readonly MacroPlayer _macroPlayer;
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly Dictionary<string, PedalDeviceInfo> _connected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string DeviceKey, int SwitchIndex), Border> _switchTiles = new();
    private readonly Dictionary<(string DeviceKey, int SwitchIndex), MacroDefinition> _activeHeldMacros = new();
    private readonly List<Button> _bankButtons = [];
    private AppProfile _profile = new();
    private CancellationTokenSource? _saveDebounce;
    private bool _loaded;
    private bool _updatingLayoutSelection;
    private int _lastOptimizedPedalCount = -1;
    private bool? _lastAutoSideBySide;
    private Point _pedalDragStart;
    private string? _draggedPedalKey;

    public MainWindow()
    {
        InitializeComponent();
        _macroPlayer = new MacroPlayer(new WindowsInputService(), _gamepad);
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        SizeChanged += MainWindow_SizeChanged;
        SourceInitialized += (_, _) => RegisterBankHotkey();
        _hid.ConnectionChanged += Hid_ConnectionChanged;
        _hid.StateChanged += Hid_StateChanged;
        _hid.Diagnostic += (_, message) => Dispatcher.Invoke(() => SetStatus(message));
        _macroPlayer.PlaybackError += (_, error) => Dispatcher.Invoke(() => SetStatus(error, true));
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
        SetStatus("Listening for Infinity / AltoEdge foot controls");
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
            }
            else
            {
                _connected.Remove(e.Device.DeviceKey);
            }
            RefreshDevices(true);
            SetStatus($"{_connected.Count} foot control{(_connected.Count == 1 ? string.Empty : "s")} connected");
        });
    }

    private void Hid_StateChanged(object? sender, PedalStateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RawReportText.Text = $"{e.Device.DisplayName}: {Convert.ToHexString(e.RawReport)}";
            var triggerId = $"{e.Device.DeviceKey}:{e.SwitchIndex}";
            var key = (e.Device.DeviceKey, e.SwitchIndex);
            if (_switchTiles.TryGetValue(key, out var tile))
            {
                tile.Background = e.IsPressed ? new SolidColorBrush(Color.FromArgb(145, 70, 205, 255)) : Brushes.Transparent;
                tile.BorderBrush = e.IsPressed ? new SolidColorBrush(Color.FromRgb(86, 220, 255)) : Brushes.Transparent;
                tile.RenderTransform = e.IsPressed ? new ScaleTransform(0.985, 0.985) : Transform.Identity;
                tile.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            if (!e.IsPressed)
            {
                if (_activeHeldMacros.Remove(key, out var heldMacro))
                {
                    _macroPlayer.Handle(triggerId, heldMacro, false);
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
                    _macroPlayer.Handle(triggerId, macro, true);
                    SetStatus($"{e.Device.DisplayName} · Pedal {e.SwitchIndex + 1} · {binding.DisplayName}");
                    break;
            }
        });
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
        EmptyState.Visibility = _profile.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var sideBySide = ShouldUseSideBySide();
        _lastAutoSideBySide = sideBySide;
        var count = _profile.Devices.Count;
        if (sideBySide)
        {
            for (var index = 0; index < Math.Max(1, count); index++)
                DevicesPanel.ColumnDefinitions.Add(new ColumnDefinition());
        }
        else
        {
            for (var index = 0; index < Math.Max(1, count); index++)
                DevicesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < count; index++)
        {
            var device = _profile.Devices[index];
            device.Normalize();
            var card = CreateDeviceCard(device, _connected.ContainsKey(device.DeviceKey));
            card.Margin = sideBySide
                ? new Thickness(index == 0 ? 0 : 7, 0, index == count - 1 ? 0 : 7, 14)
                : new Thickness(0, 0, 0, 14);
            if (sideBySide) Grid.SetColumn(card, index); else Grid.SetRow(card, index);
            DevicesPanel.Children.Add(card);
        }
        StatusDot.Fill = (Brush)FindResource(_connected.Count > 0 ? "SuccessBrush" : "MutedTextBrush");
        if (optimizeWindow) OptimizeWindowForPedals();
    }

    private Border CreateDeviceCard(PedalDeviceProfile device, bool connected)
    {
        var shell = new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"), BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(18),
            Margin = new Thickness(0), AllowDrop = true
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
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
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
        stack.Children.Add(CreateDeviceBankBar(device));
        stack.Children.Add(CreatePedalVisual(device, connected));
        return shell;
    }

    private FrameworkElement CreateDeviceBankBar(PedalDeviceProfile device)
    {
        var bar = new Border
        {
            Background = (Brush)FindResource("SurfaceAltBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(0, 0, 0, 10)
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
        var source = _profile.Devices.FirstOrDefault(device =>
            device.DeviceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase));
        var target = _profile.Devices.FirstOrDefault(device =>
            device.DeviceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
        if (source is null || target is null) return;

        var sourceIndexBeforeMove = _profile.Devices.IndexOf(source);
        var targetIndexBeforeMove = _profile.Devices.IndexOf(target);
        var position = e.GetPosition(card);
        var insertAfter = _profile.Devices.Count == 2
            ? sourceIndexBeforeMove < targetIndexBeforeMove
            : ShouldUseSideBySide()
                ? position.X >= card.ActualWidth / 2
                : position.Y >= card.ActualHeight / 2;
        _profile.Devices.Remove(source);
        var targetIndex = _profile.Devices.IndexOf(target);
        _profile.Devices.Insert(Math.Clamp(targetIndex + (insertAfter ? 1 : 0), 0, _profile.Devices.Count), source);
        RefreshDevices();
        ScheduleSave();
        SetStatus($"Moved {source.DisplayName} {(insertAfter ? "after" : "before")} {target.DisplayName}");
        e.Handled = true;
    }

    private FrameworkElement CreatePedalVisual(PedalDeviceProfile device, bool connected)
    {
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
            MaxHeight = ShouldUseSideBySide() ? 325 : 440,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = canvas
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

    private void DeviceLayoutBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _updatingLayoutSelection) return;
        if (!Enum.TryParse<PedalLayoutMode>((DeviceLayoutBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var mode))
            mode = PedalLayoutMode.Auto;
        _profile.PedalLayout = mode;
        _lastOptimizedPedalCount = -1;
        RefreshDevices(true);
        ScheduleSave();
        SetStatus($"Pedal layout: {(mode == PedalLayoutMode.SideBySide ? "side by side" : mode.ToString().ToLowerInvariant())}");
    }

    private void SetLayoutSelector()
    {
        _updatingLayoutSelection = true;
        DeviceLayoutBox.SelectedIndex = _profile.PedalLayout switch
        {
            PedalLayoutMode.Stacked => 1,
            PedalLayoutMode.SideBySide => 2,
            _ => 0
        };
        _updatingLayoutSelection = false;
    }

    private bool ShouldUseSideBySide()
    {
        if (_profile.PedalLayout == PedalLayoutMode.Stacked) return false;
        if (_profile.PedalLayout == PedalLayoutMode.SideBySide) return true;
        return _profile.Devices.Count > 1 && ActualWidth >= 1220;
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
        var count = _connected.Count;
        if (count == 0) count = Math.Min(1, _profile.Devices.Count);
        if (count == _lastOptimizedPedalCount) return;
        _lastOptimizedPedalCount = count;
        var work = SystemParameters.WorkArea;
        var sideBySide = _profile.PedalLayout switch
        {
            PedalLayoutMode.Stacked => false,
            PedalLayoutMode.SideBySide => true,
            _ => count > 1 && work.Width >= 1320
        };
        var desiredWidth = count switch
        {
            <= 1 => 1050,
            2 when sideBySide => 1510,
            2 => 1200,
            _ => sideBySide ? 1680 : 1240
        };
        var desiredHeight = count switch
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
        _profile.LearnedPedals.RemoveAll(item => item.Id.Equals(wizard.Result.Id, StringComparison.OrdinalIgnoreCase));
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
        _saveDebounce?.Cancel();
        try { _profileStore.SaveDefaultAsync(_profile).GetAwaiter().GetResult(); } catch { }
        _hotkey.Dispose(); _hid.Dispose(); _macroPlayer.Dispose(); _gamepad.Dispose(); _saveDebounce?.Dispose();
    }

    private static string ShortDeviceKey(string key) => key.Length <= 12 ? key : key[^12..];
    private static string SafeFileName(string name) => string.Concat(name.Select(character =>
        System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
