using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;

public sealed class PreparationWindow : Window
{
    public PreparationPlan? Plan { get; private set; }
    public PreparationWindow(PreparationPlan? plan, bool execution = false)
    {
        Plan = plan is null ? new PreparationPlan() : PackageJson.Clone(plan);
        Title = execution ? "Prepare this test" : "Configure preparation";
        Width = 760; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new TextBlock { Text = execution ? Plan.Title + "\nThe original value is saved before the change and restored when the run finishes."
            : "Optional setup before Send. Package authors can describe a single-row database update or an API change with capture and restoration. No credentials belong in this definition.", TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel(); var message = new TextBlock { TextWrapping = TextWrapping.Wrap }; footer.Children.Add(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var accept = new Button { Content = execution ? "Apply preparation" : "Save preparation", Style = (Style)FindResource("Primary") };
        var cancel = new Button { Content = "Cancel", IsCancel = true }; cancel.Click += (_, _) => Close();
        if (!execution) { var remove = new Button { Content = "No preparation" }; remove.Click += (_, _) => { Plan = null; DialogResult = true; }; buttons.Children.Add(remove); }
        buttons.Children.Add(cancel); buttons.Children.Add(accept); footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var json = new TextBox { Text = PackageJson.Write(Plan), AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        var value = new TextBox { Text = Plan.ValueJson };
        var flags = new List<(CheckBox Check, long Value)>();
        if (execution)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = Plan.Kind == PreparationKind.DatabaseValue ? $"Connection: {Plan.ConnectionRef}\nTarget: {Plan.Table}.{Plan.ValueColumn}\nWhere {Plan.KeyColumn} = {Plan.KeyJson}" : $"Read: {Plan.CaptureRequest.Url}\nUpdate: {Plan.UpdateRequest.Method} {Plan.UpdateRequest.Url}\nValue: {Plan.ValuePointer}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
            panel.Children.Add(new TextBlock { Text = "Value to apply (JSON scalar)", Margin = new Thickness(0, 8, 0, 4) }); panel.Children.Add(value);
            long.TryParse(Plan.ValueJson, CultureInfo.InvariantCulture, out var selected);
            foreach (var flag in Plan.Flags)
            {
                var check = new CheckBox { Content = $"{flag.Name} ({flag.Value})", IsChecked = flag.Value == 0 ? selected == 0 : (selected & flag.Value) == flag.Value, Margin = new Thickness(0, 8, 0, 0) };
                flags.Add((check, flag.Value)); panel.Children.Add(check);
                check.Click += (_, _) => value.Text = flags.Where(f => f.Check.IsChecked == true).Aggregate(0L, (v, f) => v | f.Value).ToString(CultureInfo.InvariantCulture);
            }
            if (Plan.RequireFreshLogin) panel.Children.Add(new TextBlock { Text = "After Apply, sign in again using the same identity to obtain the updated JWT.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
            root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        else root.Children.Add(json);
        accept.Click += (_, _) =>
        {
            try { if (!execution) Plan = PackageJson.Read<PreparationPlan>(json.Text); else Plan!.ValueJson = value.Text; Preparations.Validate(Plan!); DialogResult = true; }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.IO.InvalidDataException or ArgumentException) { message.Text = "Check the preparation fields and JSON scalar values. " + (ex is System.IO.InvalidDataException ? ex.Message : ""); }
        };
        Content = root;
    }
}
