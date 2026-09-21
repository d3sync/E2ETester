using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace E2ETester.Desktop;

internal sealed record ResponsePreferences(bool RedactSensitiveValues = false)
{
    public static ResponsePreferences Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ResponsePreferences>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed partial class MainViewModel
{
    private ResponsePreferences responsePreferences = new();
    public ActionCommand OpenResponseSettings { get; private set; } = null!;
    private void InitializeResponseSettings(string workspace)
    {
        var path = Path.Combine(useLocalDefaultSuite ? RememberedSessions.Root : workspace, "Settings", "responses.json");
        responsePreferences = ResponsePreferences.Load(path);
        OpenResponseSettings = new(() =>
        {
            var dialog = new Window { Title = "Response settings", Width = 560, Height = 340, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.DataContext == this) };
            var panel = new StackPanel { Margin = new Thickness(24) };
            var toggle = new CheckBox { Content = "Redact sensitive response values", IsChecked = responsePreferences.RedactSensitiveValues, Margin = new Thickness(0, 0, 0, 18) };
            panel.Children.Add(toggle);
            panel.Children.Add(new TextBlock { Text = "Applies to response bodies from future requests. Turn off to view and save values such as groupToken exactly as the API returns them. Saved runs, response files and reports will include those values. Authentication headers remain masked.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "Previously masked values cannot be recovered. Send the request again to capture them. Turning this back on does not alter earlier captures.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 18) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "Save", Style = (Style)System.Windows.Application.Current.FindResource("Primary") };
            save.Click += (_, _) =>
            {
                try
                {
                    var preference = new ResponsePreferences(toggle.IsChecked != false);
                    preference.Save(path); responsePreferences = preference;
                    Message = preference.RedactSensitiveValues ? "Future response bodies will be redacted." : "Future response bodies will be displayed and saved without redaction.";
                    dialog.Close();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(dialog, "Could not save response settings. Check disk space and access permissions.", "Response settings"); }
            };
            var cancel = new Button { Content = "Cancel", IsCancel = true }; cancel.Click += (_, _) => dialog.Close();
            buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); dialog.Content = panel; dialog.ShowDialog();
        });
    }
}
