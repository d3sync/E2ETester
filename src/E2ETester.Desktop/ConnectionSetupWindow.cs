using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;
using Microsoft.Win32;

namespace E2ETester.Desktop;

public sealed class ConnectionSetupWindow : Window
{
    private DatabaseConnectionDetails details;
    public ConnectionEditor? Connection { get; private set; }
    public ConnectionSetupWindow(string alias, DatabaseProvider provider)
    {
        details = new() { Alias = alias, Provider = provider };
        Title = "Connect to a database"; Width = 540; Height = 700; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Render();
    }
    private void Render()
    {
        DataContext = details;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Database connection", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Enter the details provided by your administrator, or import a connection template. Passwords stay on this computer.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });
        var type = new ComboBox { ItemsSource = Enum.GetValues<DatabaseProvider>(), SelectedItem = details.Provider };
        panel.Children.Add(type);
        type.SelectionChanged += (_, _) => { if (type.SelectedItem is DatabaseProvider provider && provider != details.Provider) { details.Provider = provider; Render(); } };
        Add("Connection name (matches the package)", nameof(details.Alias));
        var password = new PasswordBox();
        if (details.Provider != DatabaseProvider.Sqlite)
        {
        Add("Server", nameof(details.Host)); Add("Port (0 = default)", nameof(details.Port));
        Add("Database name", nameof(details.Database)); Add("Username", nameof(details.Username));
        var windows = new CheckBox { Content = "Windows authentication (SQL Server)", Margin = new Thickness(0, 10, 0, 0) }; windows.SetBinding(CheckBox.IsCheckedProperty, nameof(details.WindowsAuthentication)); if (details.Provider == DatabaseProvider.SqlServer) panel.Children.Add(windows);
        panel.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 10, 0, 4) });
        panel.Children.Add(password);
        }
        if (details.Provider == DatabaseProvider.Sqlite)
        {
        Add("SQLite file", nameof(details.File));
        var browse = new Button { Content = "Choose SQLite file…", Margin = new Thickness(0, 8, 0, 8) };
        browse.Click += (_, _) => { var file = new OpenFileDialog { Filter = "SQLite files|*.db;*.sqlite;*.sqlite3|All files|*.*" }; if (file.ShowDialog() == true) { details.File = file.FileName; details.Provider = DatabaseProvider.Sqlite; Render(); } }; panel.Children.Add(browse);
        }
        var readOnly = new CheckBox { Content = "This is a read-only database account", Margin = new Thickness(0, 8, 0, 8) }; readOnly.SetBinding(CheckBox.IsCheckedProperty, nameof(details.ReadOnlyAccount)); panel.Children.Add(readOnly);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var buttons = new WrapPanel();
        Button Action(string title, Action action) { var b = new Button { Content = title, Margin = new Thickness(0, 6, 6, 0) }; b.Click += (_, _) => { try { action(); } catch (Exception ex) when (ex is IOException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException) { message.Text = "Check the connection fields or template. " + (ex is InvalidDataException ? ex.Message : "The operation could not be completed."); } }; buttons.Children.Add(b); return b; }
        Action("Import template…", () => { var file = new OpenFileDialog { Filter = "Connection template|*.json" }; if (file.ShowDialog() == true) { details = PackageJson.Read<DatabaseConnectionDetails>(File.ReadAllText(file.FileName)); Render(); } });
        Action("Export template…", () => { var file = new SaveFileDialog { Filter = "Connection template|*.json", FileName = "database-connection.json" }; if (file.ShowDialog() == true) File.WriteAllText(file.FileName, PackageJson.Write(details)); });
        Action("Use connection", () => { Connection = new() { Alias = details.Alias, Provider = details.Provider, ConnectionString = details.Build(password.Password), ReadOnlyCredentialsConfirmed = details.ReadOnlyAccount }; DialogResult = true; }).Style = (Style)FindResource("Primary");
        panel.Children.Add(buttons); panel.Children.Add(message); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Add(string label, string property) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); var box = new TextBox(); box.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); panel.Children.Add(box); }
    }
}
