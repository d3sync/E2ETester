using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text.Json;
using E2ETester.Application;
using E2ETester.Core;
using E2ETester.Infrastructure;

namespace E2ETester.Desktop;

public sealed class ConnectionEditor : Observable
{
    private string alias = "local-db", connectionString = "";
    private DatabaseProvider provider;
    private bool confirmed;
    public string Alias { get => alias; set => Set(ref alias, value); }
    public DatabaseProvider Provider { get => provider; set => Set(ref provider, value); }
    public string ConnectionString { get => connectionString; set => Set(ref connectionString, value); }
    public bool ReadOnlyCredentialsConfirmed { get => confirmed; set => Set(ref confirmed, value); }
    public DatabaseConnectionProfile Profile => new(Alias, Provider, ConnectionString, ReadOnlyCredentialsConfirmed);
    public override string ToString() => Alias;
}

public sealed class DatabaseViewModel : Observable
{
    private readonly DatabaseQueries service = new(new DatabaseExecutor());
    private QueryDefinition? selectedQuery;
    private QueryDefinition draft = new();
    private ConnectionEditor? selectedConnection;
    private DataView? resultView;
    private DataRowView? selectedRow;
    private QueryEvidence? evidence;
    private CancellationTokenSource? cancellation;
    private bool busy, canSaveQueries = true;
    private string message = "Choose a connection and query. SQL dialects are provider-specific; use @name parameters.";
    public ObservableCollection<QueryDefinition> Queries { get; } = [];
    public ObservableCollection<ConnectionEditor> Connections { get; } = [];
    public ObservableCollection<QueryParameter> Parameters { get; } = [];
    public static DatabaseProvider[] Providers { get; } = Enum.GetValues<DatabaseProvider>();
    public static ParameterType[] ParameterTypes { get; } = Enum.GetValues<ParameterType>();
    public static DatabaseCheckKind[] CheckKinds { get; } = Enum.GetValues<DatabaseCheckKind>();
    public Action<FixtureSelection>? UseFixture { get; set; }
    public Func<QueryEvidence, Task>? AttachEvidence { get; set; }
    public string Message { get => message; set => Set(ref message, value); }
    public bool Busy { get => busy; private set { Set(ref busy, value); Changed(nameof(CanEdit)); Refresh(); } }
    public bool CanEdit => !Busy;
    public bool CanSaveQueries { get => canSaveQueries; set { Set(ref canSaveQueries, value); Refresh(); } }
    public QueryDefinition Draft { get => draft; private set { Set(ref draft, value); } }
    public QueryDefinition? SelectedQuery
    {
        get => selectedQuery;
        set
        {
            Set(ref selectedQuery, value); Draft = PackageJson.Clone(value ?? new QueryDefinition());
            Parameters.Clear(); foreach (var p in Draft.Parameters) Parameters.Add(p);
            SelectedConnection = Connections.FirstOrDefault(c => c.Alias == Draft.ConnectionRef);
            ClearResult();
        }
    }
    public ConnectionEditor? SelectedConnection
    {
        get => selectedConnection;
        set
        {
            if (selectedConnection is not null) selectedConnection.PropertyChanged -= ConnectionEdited;
            Set(ref selectedConnection, value);
            if (selectedConnection is not null) selectedConnection.PropertyChanged += ConnectionEdited;
            ClearResult();
        }
    }
    private void ConnectionEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ClearResult();
    public DataView? ResultView { get => resultView; private set => Set(ref resultView, value); }
    public DataRowView? SelectedRow { get => selectedRow; set { Set(ref selectedRow, value); Refresh(); } }
    public QueryEvidence? LastEvidence => evidence;
    public AsyncCommand RunQuery { get; }
    public AsyncCommand TestConnection { get; }
    public AsyncCommand Attach { get; }
    public ActionCommand Cancel { get; }
    public ActionCommand SelectFixture { get; }
    public ActionCommand NewQuery { get; }
    public ActionCommand SaveQuery { get; }
    public ActionCommand AddParameter { get; }
    public ActionCommand AddConnection { get; }
    public ActionCommand BrowseSqlite { get; }
    public string SensitiveColumnNames { get => string.Join(", ", Draft.SensitiveColumns); set { Draft.SensitiveColumns = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(); Changed(); } }

    public DatabaseViewModel()
    {
        RunQuery = new(() => ExecuteAsync(false), ex => Message = ex.Message, () => !Busy && SelectedConnection is not null);
        TestConnection = new(() => ExecuteAsync(true), ex => Message = ex.Message, () => !Busy && SelectedConnection is not null);
        Attach = new(async () => { if (evidence is not null && AttachEvidence is not null) { await AttachEvidence(evidence); Message = "Evidence attached to the selected attempt. Exploration does not satisfy required checks."; } }, ex => Message = ex.Message, () => !Busy && evidence is not null && AttachEvidence is not null);
        Cancel = new(() => cancellation?.Cancel(), () => Busy);
        SelectFixture = new(() =>
        {
            try
            {
                if (evidence is null || SelectedRow is null) return;
                if (DatabaseQueries.Fingerprint(CurrentQuery()) != DatabaseQueries.Fingerprint(evidence.Query)) throw new InvalidOperationException("Query changed after execution. Run it again before selecting a fixture.");
                UseFixture?.Invoke(DatabaseQueries.SelectFixture(evidence, (int)SelectedRow["__rowIndex"]));
                Message = "Fixture selected for the current case. Close Query Studio to review the resolved request.";
            }
            catch (Exception ex) { Message = ex.Message; }
        }, () => !Busy && SelectedRow is not null && evidence is not null && UseFixture is not null);
        NewQuery = new(() => SelectedQuery = null, () => !Busy);
        SaveQuery = new(() =>
        {
            try
            {
                var query = CurrentQuery(); DatabaseQueries.Validate(query);
                var existing = Queries.FirstOrDefault(q => q.Id == query.Id);
                if (existing is not null) Queries[Queries.IndexOf(existing)] = query; else Queries.Add(query);
                SelectedQuery = query; Message = "Query saved in this package. Export the package to preserve it.";
            }
            catch (Exception ex) { Message = ex.Message; }
        }, () => !Busy && CanSaveQueries);
        AddParameter = new(() => Parameters.Add(new QueryParameter { Name = "p" + (Parameters.Count + 1) }), () => !Busy);
        AddConnection = new(() =>
        {
            var alias = Draft.ConnectionRef;
            if (Connections.Any(c => c.Alias == alias)) alias = "connection-" + (Connections.Count + 1);
            var connection = new ConnectionEditor { Alias = alias, Provider = Draft.Provider };
            Connections.Add(connection); SelectedConnection = connection;
        }, () => !Busy);
        BrowseSqlite = new(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "SQLite database|*.db;*.sqlite;*.sqlite3|All files|*.*" };
            if (dialog.ShowDialog() == true && SelectedConnection is not null)
            {
                SelectedConnection.ConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dialog.FileName, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString();
                Changed(nameof(SelectedConnection)); Message = "SQLite file selected. It will be opened read-only.";
            }
        }, () => !Busy && SelectedConnection?.Provider == DatabaseProvider.Sqlite);
    }
    private void Refresh()
    {
        foreach (var c in new[] { RunQuery, TestConnection, Attach }) c?.Refresh();
        foreach (var c in new[] { Cancel, SelectFixture, NewQuery, SaveQuery, AddParameter, AddConnection, BrowseSqlite }) c?.Refresh();
    }
    public void LoadQueries(IEnumerable<QueryDefinition> queries)
    {
        Queries.Clear(); foreach (var q in queries) Queries.Add(PackageJson.Clone(q)); SelectedQuery = Queries.FirstOrDefault();
    }
    public void SelectQuery(string id) { if (Queries.FirstOrDefault(q => q.Id == id) is { } query) SelectedQuery = query; Refresh(); }
    public QueryDefinition CurrentQuery() => Draft with { Parameters = PackageJson.Clone(Parameters.ToList()) };
    public void RestoreQueryDraft(QueryDefinition query)
    {
        SelectedQuery = Queries.FirstOrDefault(q => q.Id == query.Id);
        Draft = PackageJson.Clone(query); Parameters.Clear(); foreach (var p in Draft.Parameters) Parameters.Add(p);
        SelectedConnection = Connections.FirstOrDefault(c => c.Alias == Draft.ConnectionRef); ClearResult();
    }
    private void ClearResult() { evidence = null; ResultView = null; SelectedRow = null; Changed(nameof(SensitiveColumnNames)); Refresh(); }
    private async Task ExecuteAsync(bool connectionOnly)
    {
        if (SelectedConnection is null) return;
        var query = connectionOnly ? new QueryDefinition { ConnectionRef = SelectedConnection.Alias, Provider = SelectedConnection.Provider, Sql = "SELECT 1 AS connection_test", RowLimit = 1 } : CurrentQuery();
        Busy = true; cancellation = new(); ClearResult();
        try
        {
            var result = await service.ExecuteAsync(query, SelectedConnection.Profile, cancellation.Token);
            if (connectionOnly) { Message = result.Result.Error ?? "Connection succeeded."; return; }
            evidence = result; Render(result.Result);
            Message = result.Result.Error ?? $"{result.Result.Rows.Count} rows · {result.Result.DurationMs} ms{(result.Result.Truncated ? " · incomplete/capped result" : "")}. Select a row to use as a fixture or attach this result as evidence.";
        }
        finally { cancellation.Dispose(); cancellation = null; Busy = false; }
    }
    private void Render(QueryResult result)
    {
        var table = new DataTable(); table.Columns.Add("__rowIndex", typeof(int));
        // Internal grid identifiers are separate from returned SQL column names.
        for (var i = 0; i < result.Columns.Count; i++) table.Columns.Add("c" + i, typeof(string)).Caption = result.Columns[i].Name;
        for (var index = 0; index < result.Rows.Count; index++)
            table.Rows.Add(new object[] { index }.Concat(result.Columns.Select(c => (object)(result.Rows[index][c.Name].ValueKind == JsonValueKind.Null ? "NULL" : ValueBindings.Text(result.Rows[index][c.Name])))).ToArray());
        ResultView = table.DefaultView;
    }
}
