using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;
internal static class UsabilityPolishSmoke
{
    internal static async Task RunAsync(Window owner, string screenshot)
    {
        var dialog = new PreparationWindow(new() { Table = "accounts", ValueJson = "0", Flags = [new("None", 0), new("Read", 4), new("Edit", 8)] }, true) { Owner = owner };
        dialog.Show(); dialog.UpdateLayout();
        var value = Children(dialog).OfType<TextBox>().Single();
        var flags = Children(dialog).OfType<CheckBox>().ToList();
        value.Text = "268"; // 256 is deliberately unknown to the displayed enum.
        if (flags[0].IsChecked == true || flags[1].IsChecked != true || flags[2].IsChecked != true) throw new InvalidOperationException("Numeric permission input did not select flags.");
        flags[1].IsChecked = false; flags[1].RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        if (value.Text != "264") throw new InvalidOperationException("Flag toggle lost unknown permission bits.");
        value.Text = "0";
        if (flags[0].IsChecked != true || flags.Skip(1).Any(f => f.IsChecked == true)) throw new InvalidOperationException("Zero permissions did not reset checkboxes.");
        value.Text = "invalid";
        if (flags.Any(f => f.IsChecked == true)) throw new InvalidOperationException("Invalid input retained misleading flags.");
        dialog.Close();
        var root = Path.Combine(Path.GetTempPath(), "E2E-polish-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var file = Path.Combine(root, "source-package-name.jsee"); File.WriteAllText(file, PackageJson.Write(SuiteDefaults.Create()));
        string runLabel;
        using (var model = new MainViewModel(Path.Combine(root, "workspace"), Path.Combine(root, "Sessions")))
        {
            model.OpenPackageFile(file);
            var connection = new ConnectionEditor { Alias = "remembered-db", Provider = DatabaseProvider.Sqlite, ConnectionString = "Data Source=test.db", RememberLocally = true };
            model.Database.SaveConnectionDefaults!(connection, false);
            model.SelectedIdentity!.Secret = "local-sign-in-placeholder";
            await model.Send.RunAsync(); runLabel = model.RunLabel;
            model.SelectedCase = model.Cases[1]; model.SelectedCase.Body = "{\"groupToken\":\"original-resumed-value\",\"note\":\"[REDACTED]\"}"; model.SelectedCase.BodySensitive = true; await model.SaveDraftAsync();
            var files = await model.SaveLocalReportsAsync();
            if (!Path.GetFileName(files.Simple).StartsWith("source-package-name_report") || !File.Exists(files.Full)) throw new InvalidOperationException("Local paired reports lost source filename.");
        }
        using (var model = new MainViewModel(Path.Combine(root, "workspace"), Path.Combine(root, "Sessions")))
        {
            await model.RestoreDraft.RunAsync();
            if (model.RunLabel != runLabel || model.SelectedCase != model.Cases[1] || !model.Database.Connections.Any(c => c.Alias == "remembered-db" && c.RememberLocally)) throw new InvalidOperationException("Restart lost run position or remembered connection.");
            if (model.SelectedCase!.Body != "{\"groupToken\":\"original-resumed-value\",\"note\":\"[REDACTED]\"}") throw new InvalidOperationException("Saving/resuming changed raw draft values.");
            var window = new MainWindow(model, restoreDraft: false) { Owner = owner }; window.Show();
            model.SelectedCase = model.Cases[1]; model.AddCheck.Execute(null); window.RequestTabs.SelectedIndex = 4;
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var picker = Children(window).OfType<ComboBox>().First(c => c.ItemsSource is CheckKind[]);
            if (!ReferenceEquals(picker.Style, window.FindResource(typeof(ComboBox)))) throw new InvalidOperationException("Check selector lost its themed style.");
            picker.IsDropDownOpen = true; window.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var image = File.Create(screenshot + ".checks.png")) encoder.Save(image);
            picker.IsDropDownOpen = false;
            model.Message = "Connection saved successfully.";
            if (window.ActivityBanner.Visibility != Visibility.Visible || window.ActivityText.Text != model.Message) throw new InvalidOperationException("Action feedback is not visible.");
            var files = await model.SaveLocalReportsAsync();
            window.Close();
            if (!Path.GetFileName(files.Simple).StartsWith("source-package-name_report")) throw new InvalidOperationException("Recovered run lost report naming.");
        }
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
}
