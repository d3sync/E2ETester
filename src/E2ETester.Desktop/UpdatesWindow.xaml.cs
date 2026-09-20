using System.Windows;
namespace E2ETester.Desktop;
public partial class UpdatesWindow : Window
{
    public UpdatesWindow(UpdatesViewModel model)
    {
        InitializeComponent(); DataContext = model;
        Closing += (_, e) => { if (model.Busy) e.Cancel = true; };
    }
}
