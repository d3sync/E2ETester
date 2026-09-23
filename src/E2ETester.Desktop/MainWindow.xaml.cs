using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace E2ETester.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool updatingToken;
    private bool closingAfterSave, autosaving, handlingClose;
    private readonly System.Windows.Threading.DispatcherTimer draftTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    public MainWindow(MainViewModel model, bool restoreDraft = true, bool recoverOnStartup = true)
    {
        viewModel = model; InitializeComponent(); DataContext = model;
        model.PropertyChanged += ModelChanged;
        if (model.SelectedCase?.Baseline.Kind == Core.TestStepKind.SwaggerAuthentication) RequestTabs.SelectedIndex = 3;
        model.OpenDatabaseRequested += ShowDatabase;
        if (restoreDraft)
        {
            Loaded += async (_, _) => { if (recoverOnStartup) await model.RestoreDraft.RunAsync(); draftTimer.Start(); await model.CheckForUpdatesOnStartupAsync(this); };
            draftTimer.Tick += async (_, _) =>
            {
                if (model.Busy || autosaving) return;
                autosaving = true;
                try { await model.SaveDraftAsync(); }
                catch { model.Message = "Draft could not be saved. Check disk space and use Save draft to retry."; }
                finally { autosaving = false; }
            };
        }
        Closing += async (_, e) =>
        {
            if (closingAfterSave || !restoreDraft) return;
            e.Cancel = true;
            if (handlingClose) return;
            if (model.Busy || model.Database.Busy) { model.Message = "Cancel the operation and wait for it to finish before closing."; return; }
            handlingClose = true; draftTimer.Stop();
            try
            {
                // A background recovery write must finish before disposing its workspace.
                while (autosaving) await Task.Delay(25);
                var choice = ExitChoice.Save;
                if (model.HasUnsavedWork)
                {
                    var prompt = new UnsavedChangesWindow { Owner = this };
                    prompt.ShowDialog(); choice = prompt.Choice;
                }
                if (choice == ExitChoice.Cancel) return;
                IsEnabled = false;
                if (await model.PrepareToExitAsync(choice)) { closingAfterSave = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
            }
            catch { model.Message = "Could not save the draft. The app is still open; retry Save draft, or choose Exit without saving."; }
            finally { handlingClose = false; if (!closingAfterSave) { IsEnabled = true; draftTimer.Start(); } }

        };
        Closed += (_, _) => { model.PropertyChanged -= ModelChanged; model.OpenDatabaseRequested -= ShowDatabase; model.Dispose(); };
    }
    private void ShowDatabase() => new DatabaseWindow(viewModel.Database) { Owner = this }.ShowDialog();
    private void OpenToolsMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu.DataContext = viewModel;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }
    // The case list stays visually enabled during a send; ignore keyboard selection until the run is editable again.
    private void CaseListKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (!viewModel.CanEdit) e.Handled = true; }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedCase))
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (viewModel.SelectedCase is { } selected) CaseList.ScrollIntoView(selected);
                CaseInstructions.ScrollToHome();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        if (e.PropertyName == nameof(MainViewModel.SelectedCase) && viewModel.SelectedCase?.Baseline.Kind == Core.TestStepKind.SwaggerAuthentication) RequestTabs.SelectedIndex = 3;
        if (e.PropertyName != nameof(MainViewModel.SelectedIdentity)) return;
        updatingToken = true; TokenBox.Password = viewModel.SelectedIdentity?.Secret ?? ""; updatingToken = false;
    }
    private void TokenChanged(object sender, RoutedEventArgs e)
    {
        if (!updatingToken && viewModel.SelectedIdentity is not null) viewModel.SelectedIdentity.Secret = ((PasswordBox)sender).Password;
    }
}
