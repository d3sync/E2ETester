using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;
public sealed partial class MainViewModel
{
    internal Func<bool>? RunAnywayConfirmation { get; set; }
    internal Func<IReadOnlyList<string>, Dictionary<string, JsonElement>?>? ManualBindingInput { get; set; }
    private RequestDefinition? ResolveRunAnywayRequest(RequestDefinition request)
    {
        var sources = GuidedActionBindings.Defaults(request, run);
        var missing = ValueBindings.Dependencies(request).Distinct().SelectMany(id => ValueBindings.CaseExpressions(request, id)).Distinct()
            .Where(expression =>
            {
                try { _ = ValueBindings.Resolve(expression, null, run: run, selectedAttempts: sources); return false; }
                catch (InvalidOperationException) { return true; }
            }).ToList();
        if (missing.Count == 0) return request;
        var values = ManualBindingInput is { } input ? input(missing) : AskForBindingValues(missing);
        if (values is null) { Message = "Request cancelled. No API request was sent."; return null; }
        foreach (var expression in missing)
            if (!values.ContainsKey(expression)) throw new InvalidOperationException("Enter a value for " + expression);
        // The resolved request is compared against the imported baseline in the attempt journal.
        return ValueBindings.Request(request, SelectedCase?.Fixture, run, sources, values);
    }
    private Dictionary<string, JsonElement>? AskForBindingValues(IReadOnlyList<string> expressions)
    {
        var dialog = new Window { Title = "Run anyway · supply missing values", Width = 620, Height = 460, Owner = PreparationOwner(), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(20) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = "These values are unavailable from saved responses. Enter the verified values to send this request without running the source cases. Numbers, true/false and null are treated as JSON; quote numeric-looking IDs to keep them as strings.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var fields = new Dictionary<string, TextBox>();
        foreach (var expression in expressions)
        {
            panel.Children.Add(new TextBlock { Text = expression, TextWrapping = TextWrapping.Wrap });
            var field = new TextBox { Margin = new Thickness(0, 6, 0, 16) }; fields.Add(expression, field); panel.Children.Add(field);
        }
        Dictionary<string, JsonElement>? result = null;
        var cancel = new Button { Content = "Cancel", IsCancel = true }; cancel.Click += (_, _) => dialog.Close();
        var send = new Button { Content = "Use values and send", Style = (Style)System.Windows.Application.Current.FindResource("Primary") };
        send.Click += (_, _) =>
        {
            if (fields.Any(p => string.IsNullOrWhiteSpace(p.Value.Text))) { MessageBox.Show(dialog, "Enter each value. Use \"\" for an empty string."); return; }
            result = fields.ToDictionary(p => p.Key, p => Parse(p.Value.Text)); dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(send); dialog.Content = root;
        return dialog.ShowDialog() == true ? result : null;
        static JsonElement Parse(string text) { try { return JsonSerializer.Deserialize<JsonElement>(text); } catch (JsonException) { return JsonSerializer.SerializeToElement(text); } }
    }
}
