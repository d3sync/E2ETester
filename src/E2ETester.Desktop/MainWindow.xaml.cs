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
    private readonly System.Windows.Threading.DispatcherTimer activityTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    // View state (open sections, last reviewer) lives outside the workspace; smoke child windows keep in-memory defaults.
    private readonly string? uiPreferencesPath;
    private UiPreferences preferences = new();
    private bool applyingPreferences;
    public MainWindow(MainViewModel model, bool restoreDraft = true, bool recoverOnStartup = true, string? shortcutSettingsPath = null, string? uiPreferencesPath = null)
    {
        viewModel = model;
        this.uiPreferencesPath = uiPreferencesPath ?? (restoreDraft ? UiPreferences.DefaultPath : null);
        preferences = this.uiPreferencesPath is null ? new() : UiPreferences.Load(this.uiPreferencesPath);
        model.LastReviewer = preferences.LastReviewer;
        applyingPreferences = true;
        InitializeComponent(); DataContext = model; InitializeShortcuts(shortcutSettingsPath);
        model.PropertyChanged += ModelChanged;
        activityTimer.Tick += (_, _) => DismissActivity();
        SizeChanged += (_, _) => UpdateToolbarLayout();
        Loaded += (_, _) => UpdateToolbarLayout();
        if (model.SelectedCase?.Baseline.Kind == Core.TestStepKind.SwaggerAuthentication) RequestTabs.SelectedIndex = 3;
        EditPackage.IsExpanded = preferences.EditPackageExpanded && model.CanAuthor;
        AttemptHistory.IsExpanded = preferences.AttemptHistoryExpanded;
        InstructionsExpander.IsExpanded = model.SelectedCase?.HasInstructions == true && !preferences.InstructionsCollapsed;
        applyingPreferences = false;
        model.OpenDatabaseRequested += ShowDatabase;
        if (restoreDraft)
        {
            WindowState = WindowState.Maximized;
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
    private void UpdateToolbarLayout()
    {
        var compact = ActualWidth < 1300;
        Grid.SetColumnSpan(ToolbarActions, compact ? 3 : 1);
        Grid.SetRow(BaseUrlPanel, compact ? 1 : 0);
        Grid.SetColumn(BaseUrlPanel, compact ? 0 : 1);
        Grid.SetColumnSpan(BaseUrlPanel, compact ? 2 : 1);
        BaseUrlPanel.Margin = compact ? new Thickness(0, 8, 12, 0) : new Thickness(20, 0, 20, 0);
        Grid.SetRow(ToolbarRunActions, compact ? 1 : 0);
        ToolbarRunActions.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
    }
    private void SetExpanded(Expander expander, bool expanded)
    {
        // Programmatic changes (case switch, run start) do not count as a tester preference.
        applyingPreferences = true;
        try { expander.IsExpanded = expanded; } finally { applyingPreferences = false; }
    }
    private void UiPreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (applyingPreferences) return;
        preferences = preferences with { EditPackageExpanded = EditPackage.IsExpanded, InstructionsCollapsed = !InstructionsExpander.IsExpanded, AttemptHistoryExpanded = AttemptHistory.IsExpanded };
        PersistPreferences();
    }
    private void PersistPreferences()
    {
        if (uiPreferencesPath is null) return;
        try { preferences.Save(uiPreferencesPath); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { /* a lost view preference is not worth interrupting the tester */ }
    }
    private void DismissActivity(object sender, RoutedEventArgs e) => DismissActivity();
    private void DismissActivity() { activityTimer.Stop(); ActivityBanner.Visibility = Visibility.Collapsed; }
    // The notification is a toast over the lower-right corner: it fades after a while, and the footer keeps the text.
    private void ShowActivity()
    {
        ActivityText.Text = viewModel.Message;
        ActivityBanner.Visibility = string.IsNullOrWhiteSpace(viewModel.Message) ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(ActivityText, System.Windows.Automation.AutomationLiveSetting.Polite);
        activityTimer.Stop(); if (ActivityBanner.Visibility == Visibility.Visible) activityTimer.Start();
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
        if (e.PropertyName == nameof(MainViewModel.Message)) ShowActivity();
        // Authoring controls fold away once a run starts; they reopen only by the tester's choice.
        if (e.PropertyName == nameof(MainViewModel.CanAuthor) && !viewModel.CanAuthor) SetExpanded(EditPackage, false);
        if (e.PropertyName == nameof(MainViewModel.LastReviewer)) { preferences = preferences with { LastReviewer = viewModel.LastReviewer }; PersistPreferences(); }
        if (e.PropertyName == nameof(MainViewModel.SelectedCase))
        {
            // Open the instructions only when the case has some; an empty box invites edits mid-run.
            SetExpanded(InstructionsExpander, viewModel.SelectedCase?.HasInstructions == true && !preferences.InstructionsCollapsed);
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (viewModel.SelectedCase is { } selected) CaseList.ScrollIntoView(selected);
                CaseInstructions.ScrollToHome();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        if (e.PropertyName == nameof(MainViewModel.SelectedCase) && viewModel.SelectedCase?.Baseline.Kind == Core.TestStepKind.SwaggerAuthentication) RequestTabs.SelectedIndex = 3;
        if (e.PropertyName != nameof(MainViewModel.SelectedIdentity)) return;
        updatingToken = true; TokenBox.Password = viewModel.SelectedIdentity?.Secret ?? ""; updatingToken = false;
    }
    private void TokenChanged(object sender, RoutedEventArgs e)
    {
        if (!updatingToken && viewModel.SelectedIdentity is not null) viewModel.SelectedIdentity.Secret = ((PasswordBox)sender).Password;
    }
}
