using System.Windows;

namespace E2ETester.Desktop;
public enum ExitChoice { Cancel, Save, WithoutSaving }
public partial class UnsavedChangesWindow : Window
{
    public ExitChoice Choice { get; private set; } = ExitChoice.Cancel;
    public UnsavedChangesWindow() => InitializeComponent();
    private void Cancel(object sender, RoutedEventArgs e) { Choice = ExitChoice.Cancel; DialogResult = false; }
    private void Save(object sender, RoutedEventArgs e) { Choice = ExitChoice.Save; DialogResult = true; }
    private void Discard(object sender, RoutedEventArgs e) { Choice = ExitChoice.WithoutSaving; DialogResult = true; }
}
