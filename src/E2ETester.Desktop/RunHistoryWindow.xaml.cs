using System.Collections.ObjectModel;
using System.Windows;
using E2ETester.Application;

namespace E2ETester.Desktop;

public partial class RunHistoryWindow : Window
{
    public RunHistoryWindow(RunHistoryViewModel model) { InitializeComponent(); DataContext = model; }
}

public sealed class RunHistoryViewModel : Observable
{
    private readonly IRunHistoryStore store;
    private int offset;
    private bool busy, hasOlder;
    private RunSummary? selected;
    private string message = "Loading saved sessions…";
    public ObservableCollection<RunSummary> Runs { get; } = [];
    public RunSummary? Selected { get => selected; set { Set(ref selected, value); Open.Refresh(); Delete?.Refresh(); } }
    public string Message { get => message; private set => Set(ref message, value); }
    public AsyncCommand Newer { get; }
    public AsyncCommand Older { get; }
    public ActionCommand Open { get; }
    public AsyncCommand Delete { get; }
    public RunHistoryViewModel(IRunHistoryStore store, Action<string> open)
    {
        this.store = store;
        Newer = new(() => LoadAsync(offset - 50), ex => Message = ex.Message, () => !busy && offset > 0);
        Older = new(() => LoadAsync(offset + 50), ex => Message = ex.Message, () => !busy && hasOlder);
        Open = new(() => { if (Selected is not null) open(Selected.Id); }, () => !busy && Selected is not null);
        Delete = new(async () =>
        {
            if (Selected is null || store is not Infrastructure.SqliteRunStore journal) return;
            if (MessageBox.Show($"Permanently remove the finished run '{Selected.Title}' started {Selected.StartedAt:u}? Export or back up its evidence first.", "Remove finished run", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await journal.DeleteFinishedAsync(Selected.Id); await LoadAsync(offset);
        }, ex => Message = ex.Message, () => !busy && Selected?.FinishedAt is not null && store is Infrastructure.SqliteRunStore);
    }
    public async Task LoadAsync(int pageOffset = 0)
    {
        busy = true; Refresh();
        try
        {
            var rows = await store.ListAsync(pageOffset, 51);
            offset = pageOffset; hasOlder = rows.Count > 50;
            Runs.Clear(); foreach (var row in rows.Take(50)) Runs.Add(row);
            Selected = Runs.FirstOrDefault();
            Message = Runs.Count == 0 ? "No saved runs yet." : $"Sessions {offset + 1}–{offset + Runs.Count} · newest first";
        }
        finally { busy = false; Refresh(); }
    }
    private void Refresh() { Newer.Refresh(); Older.Refresh(); Open.Refresh(); Delete.Refresh(); }
}
