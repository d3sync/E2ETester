using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace E2ETester.Desktop;

public partial class DatabaseWindow : Window
{
    private readonly DatabaseViewModel model;
    private bool updating;
    public DatabaseWindow(DatabaseViewModel viewModel)
    {
        model = viewModel; InitializeComponent(); DataContext = model; SyncConnection();
        model.PropertyChanged += OnChanged;
        Closing += (_, e) => { if (model.Busy) { e.Cancel = true; model.Message = "Cancel the query and wait for it to stop before closing."; } };
        Closed += (_, _) => model.PropertyChanged -= OnChanged;
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(DatabaseViewModel.SelectedConnection)) SyncConnection(); }
    private void SyncConnection() { updating = true; ConnectionBox.Password = model.SelectedConnection?.ConnectionString ?? ""; updating = false; }
    private void ConnectionChanged(object sender, RoutedEventArgs e) { if (!updating && model.SelectedConnection is not null) model.SelectedConnection.ConnectionString = ((PasswordBox)sender).Password; }
    private void ResultColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName == "__rowIndex") { e.Cancel = true; return; }
        if (model.ResultView?.Table?.Columns[e.PropertyName] is DataColumn column) e.Column.Header = column.Caption;
    }
}
