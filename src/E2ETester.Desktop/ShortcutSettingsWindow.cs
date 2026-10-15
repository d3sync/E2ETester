using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace E2ETester.Desktop;

internal sealed class ShortcutSettingsWindow : Window
{
    internal ShortcutSettingsWindow(string path)
    {
        Title = "Keyboard shortcuts"; Width = 760; Height = 760; MinWidth = 620; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var values = ShortcutPreferences.Load(path);
        var fields = new Dictionary<string, TextBox>();
        var root = new DockPanel { Margin = new Thickness(20) };
        var explanation = new TextBlock { Text = "Left hand on the keyboard, right hand on the mouse.\nClick a shortcut and press your preferred combination. Clear disables it.\nSaved for your Windows user across restarts and updates. Workspace shortcuts do not run behind dialogs.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(explanation, Dock.Top); root.Children.Add(explanation);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) }; footer.Children.Add(status);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
        var reset = new Button { Content = "Restore defaults" }; buttons.Children.Add(reset);
        var save = new Button { Content = "Save shortcuts", Style = (Style)FindResource("Primary") }; buttons.Children.Add(save);
        var cancel = new Button { Content = "Cancel", IsCancel = true }; buttons.Children.Add(cancel);
        var rows = new StackPanel(); root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        foreach (var definition in ShortcutPreferences.Definitions)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = definition.Label + "\n" + definition.Scope, VerticalAlignment = VerticalAlignment.Center });
            var field = new TextBox { IsReadOnly = true, Text = values[definition.Id].Display, ToolTip = "Focus here and press a shortcut", Margin = new Thickness(8, 0, 8, 0) };
            System.Windows.Automation.AutomationProperties.SetName(field, definition.Label + " shortcut");
            Grid.SetColumn(field, 1); row.Children.Add(field); fields.Add(definition.Id, field);
            field.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key is Key.Tab or Key.Escape) return;
                e.Handled = true;
                if (e.IsRepeat || key is >= Key.LeftShift and <= Key.RightAlt || key is Key.LWin or Key.RWin) return;
                var candidate = new Dictionary<string, ShortcutKey>(values) { [definition.Id] = new(key, Keyboard.Modifiers) };
                if (ShortcutPreferences.Validate(candidate) is { } error) { status.Text = error; return; }
                values = candidate; field.Text = values[definition.Id].Display; status.Text = "Shortcut changed. Save to apply.";
            };
            var clear = new Button { Content = "Clear" }; Grid.SetColumn(clear, 2); row.Children.Add(clear);
            clear.Click += (_, _) => { values[definition.Id] = new(Key.None, ModifierKeys.None); field.Text = "Disabled"; status.Text = "Shortcut disabled. Save to apply."; };
            rows.Children.Add(row);
        }
        reset.Click += (_, _) => { values = ShortcutPreferences.Defaults(); foreach (var field in fields) field.Value.Text = values[field.Key].Display; status.Text = "Defaults restored. Save to apply."; };
        save.Click += (_, _) =>
        {
            try { ShortcutPreferences.Save(path, values); DialogResult = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Could not save shortcuts: " + ex.Message; }
        };
        Content = root;
    }
}
