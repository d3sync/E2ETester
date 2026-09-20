using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace E2ETester.Desktop;
public sealed class TextDocumentWindow : Window
{
    public TextDocumentWindow(string title, params (string Title, string Text)[] documents)
    {
        Title = title; Width = 1000; Height = 740; MinWidth = 620; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dock = new DockPanel { Margin = new Thickness(20) }; var tabs = new TabControl();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = new Button { Content = "Copy entire tab", Margin = new Thickness(8) };
        copy.Click += (_, _) => { if (tabs.SelectedItem is TabItem { Content: TextBox text }) Clipboard.SetText(text.Text); };
        var close = new Button { Content = "Close", Margin = new Thickness(8) }; close.Click += (_, _) => Close();
        buttons.Children.Add(copy); buttons.Children.Add(close); DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        foreach (var document in documents) tabs.Items.Add(new TabItem { Header = document.Title, Content = new TextBox { Text = document.Text, IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Top, FontSize = 14, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0,12,0,0) } });
        tabs.SelectedIndex = 0; dock.Children.Add(tabs); Content = dock;
    }
}
