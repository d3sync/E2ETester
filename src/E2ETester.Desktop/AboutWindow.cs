using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace E2ETester.Desktop;
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "About " + AppInfo.Name + " " + AppInfo.VersionLabel;
        Width = 820; Height = 700; MinWidth = 620; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = new DockPanel { Margin = new Thickness(0,0,0,18) };
        heading.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/e2e-tester.png")), Width = 64, Height = 64, Margin = new Thickness(0,0,18,0), VerticalAlignment = VerticalAlignment.Top });
        var summary = new StackPanel();
        summary.Children.Add(new TextBlock { Text = AppInfo.Name + " " + AppInfo.VersionLabel, FontSize = 26, FontWeight = FontWeights.SemiBold });
        summary.Children.Add(new TextBlock { Text = "Guided API testing for Windows", Margin = new Thickness(0,6,0,8) });
        summary.Children.Add(new TextBox { Text = "Created by " + AppInfo.Creator + "\n" + AppInfo.Copyright + " · MIT License", IsReadOnly = true, BorderThickness = new Thickness(0), TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0), Background = Brushes.Transparent });
        var website = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("d3sync.org")) { NavigateUri = new Uri(AppInfo.Website) };
        website.SetResourceReference(System.Windows.Documents.Hyperlink.ForegroundProperty, "Link");
        website.RequestNavigate += (_, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { MessageBox.Show(this, "Could not open your browser. Visit " + AppInfo.Website, "Website"); }
            e.Handled = true;
        };
        var site = new TextBlock { Margin = new Thickness(0,8,0,0) }; site.Inlines.Add(website); summary.Children.Add(site);
        heading.Children.Add(summary); DockPanel.SetDock(heading,Dock.Top); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
        var copy = new Button { Content = "Copy app details" }; copy.Click += (_,_) => Clipboard.SetText(AppInfo.Details);
        var close = new Button { Content = "Close", IsCancel = true }; close.Click += (_,_) => Close();
        buttons.Children.Add(copy); buttons.Children.Add(close); DockPanel.SetDock(buttons,Dock.Bottom); root.Children.Add(buttons);
        var tabs = new TabControl();
        foreach (var entry in new[] { ("About", AppInfo.Details + "\n\nPrepare reusable API test suites, authenticate with named identities, explore requests, verify optional database evidence, and export reports for people and AI agents.\n\nThird-party components retain their own licenses. The MIT license applies to this project's code."), ("MIT license", AppInfo.ReadNotice("LICENSE")), ("Third-party notices", AppInfo.ReadNotice("THIRD_PARTY_NOTICES.md")) })
            tabs.Items.Add(new TabItem { Header = entry.Item1, Content = new TextBox { Text = entry.Item2, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, Margin = new Thickness(0,12,0,0) } });
        tabs.SelectedIndex = 0; root.Children.Add(tabs); Content = root;
    }
}
