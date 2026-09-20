using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace E2ETester.Desktop;
internal static class UsabilitySmoke
{
    public static async Task RunAsync(MainWindow window, MainViewModel model)
    {
        await model.LoadDatabaseDemo.RunAsync();
        model.BaseUrl = "https://api.example.test"; model.SelectedCase!.Url = "/whoami";
        await model.Send.RunAsync();
        if (!model.Message.Contains("Cloud test") || model.RunLabel != "NEW SESSION") throw new InvalidOperationException("Fixture validation must guide the user before creating a run.");
        await model.LoadCloudSample.RunAsync();
        if (model.Cases.Count != 2 || model.Cases.Any(c => c.FixtureQueryId.Length > 0 || c.DatabaseChecks.Count > 0) || model.ShowAdvanced) throw new InvalidOperationException("Cloud package has unexpected database dependencies.");
        model.AddQuery.Execute(null); var field = model.SelectedCase!.Query.Single();
        model.RemoveField.Execute(field);
        if (model.SelectedCase.Query.Count != 0) throw new InvalidOperationException("Cannot delete parameter.");
        model.AddHeader.Execute(null); var header = model.SelectedCase.Headers.Last();
        model.RemoveField.Execute(header);
        if (model.SelectedCase.Headers.Contains(header)) throw new InvalidOperationException("Cannot delete header.");
        model.SelectedCase.FixtureRequired = true;
        await model.SaveDraft.RunAsync(); await model.RestoreDraft.RunAsync();
        if (!model.SelectedCase!.FixtureRequired) throw new InvalidOperationException("Fixture requirement was lost during recovery.");
        model.SelectedCase.FixtureRequired = false;
        if (model.SelectedCase!.Query.Count != 0 || model.SelectedCase.Headers.Any(h => h.Name == "X-Header")) throw new InvalidOperationException("Deleted fields returned after recovery.");
        var tabStyle = (Style)window.FindResource(typeof(TabItem));
        var template = (ControlTemplate)tabStyle.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
        if (template.Triggers.OfType<Trigger>().Any(t => t.Property == UIElement.IsMouseOverProperty && string.IsNullOrEmpty(t.SourceName))) throw new InvalidOperationException("Tab hover must not include the logical content subtree.");
        if (!template.Triggers.OfType<MultiTrigger>().Any(t => t.Conditions.Cast<Condition>().Any(c => c.SourceName == "Tab" && c.Property == UIElement.IsMouseOverProperty))) throw new InvalidOperationException("Header-only hover trigger missing.");
        model.SelectedCase = model.Cases[1]; window.RequestTabs.SelectedIndex = 3;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
