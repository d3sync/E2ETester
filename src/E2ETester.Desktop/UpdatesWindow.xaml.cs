using System.Windows;
namespace E2ETester.Desktop;
public partial class UpdatesWindow : Window
{
    public UpdatesWindow(UpdatesViewModel model)
    {
        InitializeComponent(); DataContext = model;
        Loaded += async (_,_) => {if(!string.IsNullOrWhiteSpace(model.Source))await model.RefreshVersions.RunAsync();};
        Closing += (_, e) => { if (model.Busy) e.Cancel = true; };
    }
}
