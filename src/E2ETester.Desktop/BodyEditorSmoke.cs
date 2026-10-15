using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using E2ETester.Core;

namespace E2ETester.Desktop;

internal static class BodyEditorSmoke
{
    internal static async Task RunAsync(Window owner, string screenshot)
    {
        using var model = new MainViewModel(Path.Combine(Path.GetTempPath(), "e2e-body-layout-" + Guid.NewGuid().ToString("N")));
        var selected = new CaseEditor(new TestCase { Request = new() { SendBody = true, Body = "{\"name\":\"original\"}" } });
        model.Cases.Add(selected); model.SelectedCase = selected;
        var view = new RequestBodyView { DataContext = model };
        // Reproduce the limited vertical space left by long case instructions.
        var dialog = new Window { Owner = owner, Content = view, Width = 520, Height = 300, Title = "Body editor regression" };
        try
        {
            dialog.Show();
            await dialog.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            view.BodyEditor.Text = "{\"name\":\"first edit\"}";
            await dialog.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (!selected.BodyChanged || view.OriginalBodyExpander.Visibility != Visibility.Visible || view.BodyEditor.ActualHeight < 100)
                throw new InvalidOperationException("Changed body collapsed its editor.");
            view.OriginalBodyExpander.IsExpanded = true;
            dialog.UpdateLayout();
            if (view.BodyEditor.ActualHeight < 100 || view.BodyScroll.ScrollableHeight <= 0)
                throw new InvalidOperationException("Original body displaced the editable body instead of scrolling.");
            view.BodyEditor.BringIntoView(); view.BodyEditor.Focus();
            view.BodyEditor.Text = "{\"name\":\"second edit\"}";
            await dialog.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (selected.Body != view.BodyEditor.Text || selected.OriginalBody != "{\"name\":\"original\"}" || view.BodyEditor.IsReadOnly)
                throw new InvalidOperationException("Body could not be re-edited or baseline was changed.");
            var position = view.BodyEditor.TransformToAncestor(view.BodyScroll).Transform(new Point());
            if (position.Y >= view.BodyScroll.ActualHeight || position.Y + view.BodyEditor.ActualHeight <= 0)
                throw new InvalidOperationException("Body editor cannot be scrolled into view.");
            var image = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(dialog); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(screenshot + ".body-editor.png"); encoder.Save(stream);
        }
        finally { dialog.Close(); }
    }
}
