using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace E2ETester.Desktop;
internal static class ExitSmoke
{
    public static async Task RunAsync(Window owner, string path)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "E2ETester-exit-test", Guid.NewGuid().ToString("N"));
        using (var model = new MainViewModel(workspace))
        {
            if (model.HasUnsavedWork) throw new InvalidOperationException("Fresh package incorrectly dirty.");
            model.PackageTitle = "Saved on exit";
            model.SelectedCase!.Query[0].Value = "retained-edit";
            model.Database.Draft.Sql = "SELECT 'query draft'";
            await model.SaveDraftAsync();
            if (!model.HasUnsavedWork) throw new InvalidOperationException("Recovery autosave must not count as explicit save.");
            if (await model.PrepareToExitAsync(ExitChoice.Cancel) || !model.HasUnsavedWork) throw new InvalidOperationException("Cancel discarded edits.");
            if (!await model.PrepareToExitAsync(ExitChoice.Save) || model.HasUnsavedWork) throw new InvalidOperationException("Save-and-exit did not save the current draft.");
            model.PackageTitle = "Not saved";
            if (!await model.PrepareToExitAsync(ExitChoice.WithoutSaving) || !model.HasUnsavedWork) throw new InvalidOperationException("Exit without saving incorrectly marked work saved.");
        }
        using (var restored = new MainViewModel(workspace))
        {
            await restored.RestoreDraft.RunAsync();
            if (restored.HasUnsavedWork || restored.PackageTitle != "Saved on exit" || restored.SelectedCase?.Query[0].Value != "retained-edit" || restored.Database.Draft.Sql != "SELECT 'query draft'") throw new InvalidOperationException("Saved exit draft failed to restore.");
            restored.SelectedCase.Query[0].Value = "new edit";
            await restored.SaveDraft.RunAsync();
            if (restored.HasUnsavedWork) throw new InvalidOperationException("Explicit Save draft failed to clear dirty state.");
        }
        // Exercise the actual WPF Closing handler and its modal choices, not just the save policy.
        var closingModel = new MainViewModel(Path.Combine(workspace, "window"));
        var closingWindow = new MainWindow(closingModel) { Owner = owner, ShowInTaskbar = false };
        closingWindow.Show(); await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        closingModel.PackageTitle = "Real close flow";
        void Choose(string caption) => owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<UnsavedChangesWindow>().Single();
            FindButton(dialog, caption)!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        }));
        Choose("Cancel"); closingWindow.Close();
        if (!closingWindow.IsVisible || !closingModel.HasUnsavedWork) throw new InvalidOperationException("Actual close Cancel failed.");
        Choose("Save and exit"); closingWindow.Close();
        await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (closingWindow.IsVisible) throw new InvalidOperationException("Actual save-and-exit did not close.");
        using (var check = new MainViewModel(Path.Combine(workspace, "window")))
        {
            await check.RestoreDraft.RunAsync();
            if (check.PackageTitle != "Real close flow") throw new InvalidOperationException("Actual close did not persist edits.");
        }
        var prompt = new UnsavedChangesWindow { Owner = owner, ShowInTaskbar = false };
        prompt.Show(); await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); prompt.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)prompt.ActualWidth, (int)prompt.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(prompt);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path + ".exit.png")) png.Save(stream);
        prompt.Close();
        if (prompt.Choice != ExitChoice.Cancel) throw new InvalidOperationException("Dismissal must default to Cancel.");
    }
    private static System.Windows.Controls.Button? FindButton(DependencyObject root, string caption)
    {
        if (root is System.Windows.Controls.Button button && button.Content?.ToString() == caption) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindButton(VisualTreeHelper.GetChild(root, i), caption) is { } found) return found;
        return null;
    }
}
