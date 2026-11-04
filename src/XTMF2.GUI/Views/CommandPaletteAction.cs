using System;

namespace XTMF2.GUI.Views;

/// <summary>
/// An action displayed by <see cref="CommandPaletteDropdown"/>.
/// </summary>
public sealed class CommandPaletteAction
{
    public string DisplayName { get; }
    public string? CommandType { get; }
    public string? Shortcut { get; }
    public bool IsEnabled { get; }
    public bool HasCommandType => !string.IsNullOrWhiteSpace(CommandType);
    public bool HasShortcut => !string.IsNullOrWhiteSpace(Shortcut);
    public double VisualOpacity => IsEnabled ? 1.0 : 0.45;
    public Action Execute { get; }

    public CommandPaletteAction(
        string displayName,
        Action execute,
        string? commandType = null,
        string? shortcut = null,
        bool isEnabled = true)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        CommandType = commandType;
        Shortcut = shortcut;
        IsEnabled = isEnabled;
    }
}
