using System.Text.RegularExpressions;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public sealed record MacroVariableContext(
    string Profile,
    string Device,
    int Pedal,
    int Bank,
    string Application,
    string Clipboard);

public static partial class MacroVariableExpander
{
    public static MacroDefinition Expand(
        MacroDefinition source,
        MacroVariableContext context,
        IEnumerable<TippyVariable> customVariables)
    {
        var result = source.Clone();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = DateTime.Now.ToString("d"),
            ["time"] = DateTime.Now.ToString("T"),
            ["datetime"] = DateTime.Now.ToString("G"),
            ["clipboard"] = context.Clipboard,
            ["app"] = context.Application,
            ["profile"] = context.Profile,
            ["device"] = context.Device,
            ["pedal"] = context.Pedal.ToString(),
            ["bank"] = context.Bank.ToString()
        };
        foreach (var variable in customVariables) values[variable.Name] = variable.Value;
        foreach (var step in result.Steps)
        {
            step.Value = Replace(step.Value, values);
            step.Arguments = Replace(step.Arguments, values);
            step.WorkingDirectory = Replace(step.WorkingDirectory, values);
        }
        return result;
    }

    private static string? Replace(string? source, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(source)) return source;
        return VariablePattern().Replace(source, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
    private static partial Regex VariablePattern();
}
