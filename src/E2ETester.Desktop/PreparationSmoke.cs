using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using E2ETester.Application;
using E2ETester.Core;
using Microsoft.Data.Sqlite;

namespace E2ETester.Desktop;

internal static class PreparationSmoke
{
    internal static async Task RunAsync(MainWindow owner, string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "preparation-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "test.db") }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE accounts(id INTEGER PRIMARY KEY, permissions INTEGER); INSERT INTO accounts VALUES(1,15)"; command.ExecuteNonQuery(); }
        var package = SuiteDefaults.Create(); package.SchemaVersion = "1.5";
        package.Cases[0].Preparation = new PreparationPlan { Title = "Choose test permissions", Provider = DatabaseProvider.Sqlite, ConnectionRef = "admin-db", Table = "accounts", KeyJson = "1", ValueColumn = "permissions", ValueJson = "4", RequireFreshLogin = true, JwtClaim = "permissions", Flags = [new("Read", 4), new("Edit", 8)] };
        var file = Path.Combine(root, "test.jsee"); File.WriteAllText(file, PackageJson.Write(package));
        var model = new MainViewModel(Path.Combine(root, "workspace"), Path.Combine(root, "sessions"));
        var window = new MainWindow(model, restoreDraft: false) { Owner = owner }; window.Show();
        model.OpenPackageFile(file);
        model.Database.Connections.Add(new ConnectionEditor { Alias = "admin-db", Provider = DatabaseProvider.Sqlite, ConnectionString = connectionString });
        var identity = model.SelectedIdentity!; identity.Secret = "previous-ui-token"; var profile = identity.BrowserProfileKey;
        async Task Prepare()
        {
            identity.Secret = "previous-ui-token";
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = System.Windows.Application.Current.Windows.OfType<PreparationWindow>().Single();
                Capture(dialog, path + ".prepare-dialog.png");
                Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "Apply preparation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }), DispatcherPriority.ApplicationIdle);
            await model.PrepareCase.RunAsync();
        }
        await Prepare();
        if (!model.HasPendingPreparation || model.SelectedIdentity != identity || identity.BrowserProfileKey != profile || identity.Secret != "previous-ui-token" || model.Identities.Any(i => i.IsSnapshot))
            throw new InvalidOperationException("Preparation must retain the same identity and browser profile without clearing the JWT or creating timestamp identities.");
        var protectedConnection = model.Database.Connections.Single(c => c.Alias == "admin-db");
        model.Database.SelectedConnection = protectedConnection;
        if (model.Database.EditConnection.CanExecute(null) || model.Database.DeleteConnection.CanExecute(null)) throw new InvalidOperationException("Active preparation connection was editable.");
        var preparedCase = model.SelectedCase;
        model.SelectedCase = model.Cases[1];
        typeof(MainViewModel).GetMethod("EnsurePreparationScope", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(model, null);
        model.SelectedCase = preparedCase;
        var runField = typeof(MainViewModel).GetField("run", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var preparedRun = runField.GetValue(model);
        runField.SetValue(model, new TestRun());
        await model.Send.RunAsync();
        if (!model.Message.Contains("needs a valid JWT")) throw new InvalidOperationException("A mismatched JWT was accepted in another run.");
        runField.SetValue(model, preparedRun);
        var restart = UpdatesViewModel.RestartArguments(Path.Combine(root, "workspace with spaces"), true);
        if (restart.Length != 2 || restart[0] != "--workspace=" + Path.Combine(root, "workspace with spaces") || restart[1] != "--dark") throw new InvalidOperationException("Update restart lost its workspace.");
        identity.Secret = "previous-ui-token"; await model.Send.RunAsync();
        if (model.SelectedAttempt is not null) throw new InvalidOperationException("Old token was accepted after preparation.");
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"permissions\":\"4\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        identity.Secret = "e30." + payload + ".local-ui-signature"; await model.Send.RunAsync();
        if (model.SelectedAttempt?.Verdict != Verdict.Pass) throw new InvalidOperationException("Fresh-token preparation flow failed: " + model.Message);
        using (var conflict = new SqliteConnection(connectionString))
        { conflict.Open(); using var command = conflict.CreateCommand(); command.CommandText = "UPDATE accounts SET permissions=7 WHERE id=1"; command.ExecuteNonQuery(); }
        var blocked = false;
        try { await model.PrepareForUpdateAsync(); } catch (InvalidOperationException) { blocked = true; }
        if (!blocked || !model.HasPendingPreparation) throw new InvalidOperationException("Update preflight accepted a restoration conflict.");
        using (var conflict = new SqliteConnection(connectionString))
        { conflict.Open(); using var command = conflict.CreateCommand(); command.CommandText = "UPDATE accounts SET permissions=4 WHERE id=1"; command.ExecuteNonQuery(); }
        var unreadable = typeof(MainViewModel).GetField("preparationRecoveryUnreadable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        unreadable.SetValue(model, true); blocked = false;
        try { await model.PrepareForUpdateAsync(); } catch (InvalidOperationException) { blocked = true; }
        finally { unreadable.SetValue(model, false); }
        if (!blocked) throw new InvalidOperationException("Update preflight accepted unreadable recovery.");
        var issuedToken = identity.Secret;
        await model.PrepareForUpdateAsync();
        if (identity.Secret != issuedToken) throw new InvalidOperationException("Restoration invalidated a reusable JWT.");
        if (model.HasPendingPreparation) throw new InvalidOperationException("Restoration was not completed: " + model.Message);
        await Prepare();
        window.Close();
        // Simulate restarting while a preparation is active; do not perform another apply.
        using var reopened = new MainViewModel(Path.Combine(root, "workspace"), Path.Combine(root, "sessions"));
        reopened.OpenPackageFile(file);
        await reopened.Send.RunAsync();
        if (reopened.SelectedAttempt?.Verdict != Verdict.Pass) throw new InvalidOperationException("A reopened package did not reuse its matching saved permission identity: " + reopened.Message);
        reopened.Database.Connections.Add(new ConnectionEditor { Alias = "admin-db", Provider = DatabaseProvider.Sqlite, ConnectionString = connectionString });
        if (!reopened.HasPendingPreparation) throw new InvalidOperationException("Preparation recovery did not survive restart.");
        await reopened.RestorePreparation.RunAsync();
        using (var connection = new SqliteConnection(connectionString))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT permissions FROM accounts WHERE id=1"; if ((long)command.ExecuteScalar()! != 15) throw new InvalidOperationException("Original permissions were not restored after restart."); }
        var profileToEdit = reopened.Database.Connections.Single(c => c.Alias == "admin-db");
        reopened.Database.SelectedConnection = profileToEdit;
        if (!reopened.Database.EditConnection.CanExecute(null) || !reopened.Database.DeleteConnection.CanExecute(null)) throw new InvalidOperationException("Restored profileToEdit cannot be managed.");
        var edited = new ConnectionEditor { Alias = profileToEdit.Alias, Provider = profileToEdit.Provider, ConnectionString = profileToEdit.ConnectionString, ReadOnlyCredentialsConfirmed = true };
        reopened.Database.ReplaceConnection(profileToEdit, edited);
        if (reopened.Database.SelectedConnection != edited || !edited.ReadOnlyCredentialsConfirmed) throw new InvalidOperationException("Connection edit was not applied.");
        reopened.Database.RemoveConnection(edited);
        if (reopened.Database.SelectedConnection is not null || reopened.Database.Connections.Count != 0 || reopened.Database.EditConnection.CanExecute(null)) throw new InvalidOperationException("Connection deletion left a stale selection.");
        var setup = new ConnectionSetupWindow(new ConnectionEditor { Alias = "fixtures", Provider = DatabaseProvider.MySql, ConnectionString = "Server=localhost;Database=example;User ID=tester;Password=local-test;SslMode=Required;AllowUserVariables=True" }) { Owner = owner }; setup.Show();
        await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture(setup, path + ".connection-wizard.png");
        if (Descendants(setup).OfType<PasswordBox>().Single().Password != "local-test") throw new InvalidOperationException("Edit connection lost its password.");
        setup.Close();
        if (setup.Connection is not null) throw new InvalidOperationException("Closing editor without saving applied changes.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Capture(FrameworkElement view, string path)
    {
        view.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
