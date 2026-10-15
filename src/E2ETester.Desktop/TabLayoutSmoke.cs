using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace E2ETester.Desktop;
internal static class TabLayoutSmoke
{
    internal static async Task RunAsync(Window owner, string screenshot)
    {
        using var model = new MainViewModel(Path.Combine(Path.GetTempPath(), "e2e-tab-layout-" + Guid.NewGuid().ToString("N")));
        model.ShowAdvanced = true;
        model.SelectedCase = model.Cases.First(c => c.IsHttpStep);
        model.SelectedCase.Instructions = string.Join("\n", Enumerable.Repeat("Review this request, inspect its response and verify the expected result.", 20));
        model.SelectedCase.Body = "{\"edited\":true}";
        model.AddQuery.Execute(null); model.AddHeader.Execute(null); model.AddCheck.Execute(null); model.AddDatabaseCheck.Execute(null);
        var window = new MainWindow(model, restoreDraft: false) { Owner = owner, Width = 1120, Height = 720 };
        try
        {
            window.Show();
            foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
            {
                window.WindowState = state;
                await Idle();
                if (window.BaseUrlPanel.ActualWidth < 180) throw new InvalidOperationException("Toolbar hid the base URL.");
                foreach (TabItem tab in window.RequestTabs.Items)
                {
                    window.RequestTabs.SelectedItem = tab; await Idle();
                    window.RequestTabs.BringIntoView(); await Idle();
                    if (window.RequestTabs.ActualHeight < 230 || tab.Content is not FrameworkElement content || content.ActualHeight < 100)
                        throw new InvalidOperationException($"Request tab {tab.Header} collapsed at {state}.");
                    foreach (var grid in Descendants(content).OfType<DataGrid>())
                        if (grid.IsVisible && grid.ActualHeight < 65) throw new InvalidOperationException($"Grid in {tab.Header} lost its edit area.");
                }
                foreach (TabItem tab in window.EvidenceTabs.Items)
                {
                    window.EvidenceTabs.SelectedItem = tab; await Idle();
                    if (window.EvidenceTabs.ActualHeight < 210) throw new InvalidOperationException($"Evidence tab {tab.Header} collapsed.");
                }
                window.AttemptHistory.IsExpanded = true; await Idle();
                if (window.EvidenceTabs.ActualHeight < 210 || window.EvidencePanelScroll.ScrollableHeight < 0)
                    throw new InvalidOperationException("History collapsed the response area.");
                window.AttemptHistory.IsExpanded = false;
                window.RequestTabs.SelectedIndex = 2; window.EvidenceTabs.SelectedIndex = 0; await Idle();
                window.RequestTabs.BringIntoView(); await Idle();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(screenshot + ".tabs-" + state + ".png"); encoder.Save(file);
            }
        }
        finally { window.Close(); }
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
