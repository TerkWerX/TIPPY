using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Tippy.App.Services;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class VariableManagerWindow : Window
{
    private readonly ObservableCollection<TippyVariable> _variables;
    private readonly string _profileName;

    public VariableManagerWindow(IEnumerable<TippyVariable> variables, string profileName)
    {
        InitializeComponent();
        _profileName = profileName;
        _variables = new ObservableCollection<TippyVariable>(variables.Select(variable =>
            new TippyVariable { Name = variable.Name, Value = variable.Value }));
        VariablesGrid.ItemsSource = _variables;
        Result = _variables.ToArray();
        BuiltInsText.Text = "{date}  " + DateTime.Now.ToString("d") + "\n{time}  " + DateTime.Now.ToString("T") +
                            "\n{datetime}  " + DateTime.Now.ToString("G") +
                            "\n{clipboard}\n{app}\n{profile}\n{device}\n{pedal}\n{bank}";
        VariablesGrid.SelectedIndex = _variables.Count > 0 ? 0 : -1;
        RefreshPreview();
    }

    public IReadOnlyList<TippyVariable> Result { get; private set; }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var suffix = 1;
        var name = "variable";
        while (_variables.Any(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"variable{++suffix}";
        var item = new TippyVariable { Name = name, Value = string.Empty };
        _variables.Add(item);
        VariablesGrid.SelectedItem = item;
        VariablesGrid.ScrollIntoView(item);
        RefreshPreview();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (VariablesGrid.SelectedItem is not TippyVariable source) return;
        var copy = new TippyVariable { Name = source.Name + "Copy", Value = source.Value };
        copy.Normalize();
        _variables.Add(copy);
        VariablesGrid.SelectedItem = copy;
        RefreshPreview();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (VariablesGrid.SelectedItem is TippyVariable selected) _variables.Remove(selected);
        RefreshPreview();
    }

    private void VariablesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RefreshPreview));

    private void VariablesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshPreview();
    private void PreviewSourceBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        if (PreviewResultText is null) return;
        TokenText.Text = VariablesGrid.SelectedItem is TippyVariable selected ? $"{{{selected.Name}}}" : "Select a variable";
        var macro = new MacroDefinition { Steps = [new MacroStep { Type = MacroStepType.Text, Value = PreviewSourceBox.Text }] };
        var expanded = MacroVariableExpander.Expand(macro,
            new MacroVariableContext(_profileName, "Example pedal", 1, 1, "Example app", "Clipboard preview"), _variables);
        PreviewResultText.Text = expanded.Steps[0].Value;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        VariablesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        VariablesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var variable in _variables) variable.Normalize();
        var duplicate = _variables.GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            MessageBox.Show(this, $"The variable name '{duplicate.Key}' is used more than once.", "Named variables", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = _variables.Select(variable => new TippyVariable { Name = variable.Name, Value = variable.Value }).ToArray();
        DialogResult = true;
    }
}
