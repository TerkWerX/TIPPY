using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tippy.App.Services;
using Tippy.Core.Models;
using Tippy.Core.Output;

namespace Tippy.App;

public partial class MidiSetupWindow : Window
{
    private readonly string _preferredOutputName;

    public MidiSetupWindow(MidiOutputSettings settings)
    {
        InitializeComponent();
        _preferredOutputName = settings.PreferredOutputName;
        Result = settings.Clone();
        RefreshOutputs();
    }

    public MidiOutputSettings Result { get; private set; }

    private void RefreshOutputs(string? preferredOutputName = null)
    {
        preferredOutputName ??= _preferredOutputName;
        var outputs = MidiOutputService.GetOutputDevices().ToList();
        if (!string.IsNullOrWhiteSpace(preferredOutputName) && outputs.All(output =>
                !output.Name.Equals(preferredOutputName, StringComparison.OrdinalIgnoreCase)))
        {
            outputs.Add(new MidiOutputService.OutputDevice(-2, preferredOutputName, IsAvailable: false));
        }
        OutputCombo.ItemsSource = outputs;
        OutputCombo.SelectedItem = string.IsNullOrWhiteSpace(preferredOutputName)
            ? outputs.FirstOrDefault(output => output.IsSystemDefault)
            : outputs.FirstOrDefault(output => output.Name.Equals(preferredOutputName, StringComparison.OrdinalIgnoreCase));
        UpdateAvailability();
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        var selected = OutputCombo.SelectedItem as MidiOutputService.OutputDevice;
        RefreshOutputs(selected?.IsSystemDefault == true ? string.Empty : selected?.Name);
        MidiStatusText.Text = "MIDI outputs rescanned.";
        MidiStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (OutputCombo.SelectedItem is not MidiOutputService.OutputDevice selected || !selected.IsAvailable) return;
        TestButton.IsEnabled = false;
        MidiStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
        MidiStatusText.Text = $"Sending a short middle-C note to {selected.DisplayName}…";
        using var output = new MidiOutputService();
        output.Configure(selected.IsSystemDefault ? string.Empty : selected.Name);
        var noteOn = MidiMessageParser.Parse("note:1:60:96");
        try
        {
            output.Send(noteOn);
            await Task.Delay(180);
            output.Send(noteOn.ToNoteOff());
            MidiStatusText.Foreground = (Brush)FindResource("SuccessBrush");
            MidiStatusText.Text = $"MIDI test passed through {output.ActiveOutputName}: note-on and note-off were both accepted by Windows.";
        }
        catch (Exception exception)
        {
            try { output.Send(noteOn.ToNoteOff()); } catch { }
            MidiStatusText.Foreground = (Brush)FindResource("DangerBrush");
            MidiStatusText.Text = exception.Message;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void OutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAvailability();

    private void UpdateAvailability()
    {
        var selected = OutputCombo.SelectedItem as MidiOutputService.OutputDevice;
        TestButton.IsEnabled = selected?.IsAvailable == true;
        if (selected is { IsAvailable: false })
        {
            MidiStatusText.Foreground = (Brush)FindResource("DangerBrush");
            MidiStatusText.Text = "That saved MIDI output is not connected. Select another output or reconnect it.";
        }
        else if (selected is not null)
        {
            MidiStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
            MidiStatusText.Text = $"Ready to send a short middle-C test note to {selected.DisplayName}.";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (OutputCombo.SelectedItem is not MidiOutputService.OutputDevice selected) return;
        if (!selected.IsAvailable)
        {
            MessageBox.Show(this, "Select a connected MIDI output before saving.", "MIDI output",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = new MidiOutputSettings
        {
            PreferredOutputName = selected.IsSystemDefault ? string.Empty : selected.Name
        };
        DialogResult = true;
    }
}
