using System.Collections.ObjectModel;
using System.Windows;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;

public partial class ManualAssessmentWindow : Window
{
    public ManualAssessmentWindow(AssessmentEditor model)
    {
        InitializeComponent(); DataContext = model;
        Closing += (_, e) => { if (model.Saving) e.Cancel = true; };
    }
}

public sealed record AssessmentHistoryItem(string Heading, string Details, string Reason);
public sealed class AssessmentEditor : Observable
{
    private readonly Attempt attempt;
    private readonly bool writable;
    private string reviewer = "", reason = "", message;
    private Verdict verdict = Verdict.Inconclusive;
    private bool saving;
    public static Verdict[] Verdicts { get; } = Enum.GetValues<Verdict>();
    public string CaseTitle => attempt.CaseTitle;
    public string AutomatedLabel => $"Automated: {attempt.Verdict.ToString().ToUpperInvariant()} · {attempt.State}";
    public string Reviewer { get => reviewer; set { Set(ref reviewer, value); Record.Refresh(); } }
    public string Reason { get => reason; set { Set(ref reason, value); Record.Refresh(); } }
    public Verdict Verdict { get => verdict; set => Set(ref verdict, value); }
    public string Message { get => message; private set => Set(ref message, value); }
    public bool Saving { get => saving; private set { Set(ref saving, value); Changed(nameof(CanWrite)); Record.Refresh(); } }
    public bool CanWrite => writable && !Saving;
    public ObservableCollection<AssessmentHistoryItem> History { get; } = [];
    public AsyncCommand Record { get; }
    public AssessmentEditor(Attempt attempt, bool writable, Func<string, Verdict, string, Task> record)
    {
        this.attempt = attempt; this.writable = writable;
        message = writable ? "A reviewer and reason are required." : "This run is finished. Assessment history is read-only.";
        Record = new(async () =>
        {
            Saving = true;
            try { await record(Reviewer, Verdict, Reason); Reason = ""; Reload(); Message = "Assessment recorded. The automated result is unchanged."; }
            finally { Saving = false; }
        }, _ => Message = "Assessment could not be recorded. Check the run state and storage, then retry.", () => CanWrite && !string.IsNullOrWhiteSpace(Reviewer) && !string.IsNullOrWhiteSpace(Reason));
        Reload();
    }
    private void Reload()
    {
        History.Clear();
        foreach (var item in attempt.ManualAssessments.AsEnumerable().Reverse())
            History.Add(new($"{item.Verdict.ToString().ToUpperInvariant()} · {item.Reviewer}", $"{item.CreatedAt:u} · Self-declared reviewer · {(ManualAssessments.IsCurrent(attempt, item) ? "Evidence unchanged" : "Evidence changed — review again")}", item.Reason));
    }
}
