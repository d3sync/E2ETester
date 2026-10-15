using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace E2ETester.Desktop;

public partial class MainWindow
{
    private sealed record Shortcut(string Id, ShortcutKey Gesture, string Label, ICommand Command);
    private List<Shortcut> shortcuts = [];
    private string shortcutSettingsPath = ShortcutPreferences.DefaultPath;
    private readonly Dictionary<Button, object?> originalShortcutHints = [];

    private void LoadShortcuts()
    {
        var values = ShortcutPreferences.Load(shortcutSettingsPath);
        var commands = new Dictionary<string, ICommand> {
            ["send"] = viewModel.Send, ["next"] = viewModel.Next, ["previous"] = viewModel.Previous,
            ["open"] = viewModel.Import, ["save"] = viewModel.SaveDraft, ["prepare"] = viewModel.PrepareCase,
            ["actions"] = viewModel.OpenGuidedActions, ["database"] = viewModel.OpenDatabase,
            ["report"] = viewModel.ViewReport, ["cancel"] = viewModel.Cancel, ["signin"] = viewModel.OpenSwagger,
            ["help"] = new ActionCommand(ShowShortcuts)
        };
        shortcuts = ShortcutPreferences.Definitions.Where(d => d.Scope == "Workspace")
            .Select(d => new Shortcut(d.Id, values[d.Id], d.Label, commands[d.Id])).ToList();
    }
    private void InitializeShortcuts(string? settingsPath)
    {
        shortcutSettingsPath = settingsPath ?? ShortcutPreferences.DefaultPath;
        LoadShortcuts();
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HandleShortcut(key, Keyboard.Modifiers, e.IsRepeat)) e.Handled = true;
        };
        Loaded += (_, _) => AddShortcutHints(this);
    }

    // Preview handling works inside multiline editors without stealing plain Enter,
    // ordinary text editing, or Escape used to dismiss a popup / cancel a grid edit.
    internal bool HandleShortcut(Key key, ModifierKeys modifiers, bool repeat = false)
    {
        var shortcut = shortcuts.FirstOrDefault(s => s.Gesture.Key != Key.None && s.Gesture.Key == key && s.Gesture.Modifiers == modifiers);
        if (shortcut is null) return false;
        if (repeat || !shortcut.Command.CanExecute(null)) return true;
        // DataGrid cells use deferred bindings: send/save must see the current edit.
        if (Keyboard.FocusedElement is DependencyObject focused)
        {
            for (var element = focused; element is not null; element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
                if (element is DataGrid grid)
                {
                    if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return true;
                    break;
                }
            if (focused is TextBox text) text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
        if (shortcut.Command.CanExecute(null)) shortcut.Command.Execute(null);
        return true;
    }

    private void AddShortcutHints(DependencyObject parent)
    {
        if (parent is Button button && shortcuts.FirstOrDefault(s => ReferenceEquals(s.Command, button.Command)) is { } shortcut)
        {
            originalShortcutHints.TryAdd(button, button.ToolTip);
            button.ToolTip = $"{originalShortcutHints[button] ?? shortcut.Label}" + (shortcut.Gesture.Key == Key.None ? "" : $" ({shortcut.Gesture.Display})");
            System.Windows.Automation.AutomationProperties.SetAcceleratorKey(button, shortcut.Gesture.Key == Key.None ? "" : shortcut.Gesture.Display);
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) AddShortcutHints(VisualTreeHelper.GetChild(parent, i));
    }

    private void ShowShortcuts()
    {
        if (new ShortcutSettingsWindow(shortcutSettingsPath) { Owner = this }.ShowDialog() != true) return;
        LoadShortcuts(); AddShortcutHints(this);
        viewModel.Message = "Keyboard shortcuts saved and applied.";
    }

    private void OpenShortcuts(object sender, RoutedEventArgs e) => ShowShortcuts();
}
