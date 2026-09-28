using System.Windows;
using System.Windows.Controls;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;

public sealed class GuidedActionsWindow : Window
{
    public GuidedActionsWindow(MainViewModel model, IReadOnlyList<TestCase> actions)
    {
        Title = "Guided Actions"; Width = 900; Height = 740; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Guided Actions", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Optional cleanup and permission tools. These do not count as test cases. Every request is recorded in Saved runs; permission changes retain restoration data.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        var selection = new ComboBox { ItemsSource = actions, DisplayMemberPath = "Title", SelectedIndex = actions.Count > 0 ? 0 : -1 }; header.Children.Add(selection);
        var instructions = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new Thickness(0, 10, 0, 10) }; header.Children.Add(instructions);
        var identity = new ComboBox { ItemsSource = model.Identities, DisplayMemberPath = "Alias" }; header.Children.Add(new TextBlock { Text = "Identity" }); header.Children.Add(identity);
        TestRun? source = model.GuidedActionSource;
        var sourceLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void SourceLabel() => sourceLabel.Text = "Response values from: " + (source is null ? "none — enter verified values manually" : source.Package.Title + " · " + source.Id);
        SourceLabel(); header.Children.Add(sourceLabel);
        var chooseSource = new Button { Content = "Choose saved source run…", HorizontalAlignment = HorizontalAlignment.Left }; header.Children.Add(chooseSource);
        var attemptsPanel = new StackPanel(); header.Children.Add(attemptsPanel);
        Action? resetRequest = null;
        var chosenAttempts = new Dictionary<string, string>();
        void LoadAttempts(TestCase? action)
        {
            attemptsPanel.Children.Clear(); chosenAttempts.Clear();
            if (action is null || action.Preparation is not null) return;
            foreach (var pair in GuidedActionBindings.Defaults(action.Request, source)) chosenAttempts[pair.Key] = pair.Value;
            foreach (var caseId in ValueBindings.Dependencies(action.Request).Distinct())
            {
                attemptsPanel.Children.Add(new TextBlock { Text = "Resource response from " + caseId + " (test verdict does not block cleanup)", TextWrapping = TextWrapping.Wrap });
                var options = GuidedActionBindings.Candidates(source, caseId, action.Request).Select(a => new { a.Id, Label = $"{a.StartedAt:yyyy-MM-dd HH:mm:ss} · HTTP {a.Response!.StatusCode} · {a.Verdict} · {a.Id}" }).ToList();
                if (options.Count == 0) attemptsPanel.Children.Add(new TextBlock { Text = "No response here contains the required value. Use Find saved resource or enter the verified target manually.", TextWrapping = TextWrapping.Wrap });
                var picker = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = chosenAttempts.GetValueOrDefault(caseId) };
                picker.SelectionChanged += (_, _) => { if (picker.SelectedValue is string id) chosenAttempts[caseId] = id; resetRequest?.Invoke(); };
                attemptsPanel.Children.Add(picker);
            }
        }
        var findResource = new Button { Content = "Find saved resource", HorizontalAlignment = HorizontalAlignment.Left }; header.Children.Add(findResource);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = new TextBox { FontSize = 15, Padding = new Thickness(12), BorderThickness = new Thickness(2), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; status.SetResourceReference(Control.BorderBrushProperty, "Link"); footer.Children.Add(status);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var preview = new Button { Content = "Resolve saved values" }; buttons.Children.Add(preview);
        var execute = new Button { Content = "Review and run action…", Style = (Style)FindResource("Primary") }; buttons.Children.Add(execute);
        var restore = new Button { Content = "Restore original permissions / prepared changes" }; buttons.Children.Add(restore);
        var cancel = new Button { Content = "Cancel waiting", Command = model.Cancel }; buttons.Children.Add(cancel);
        var editor = new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 12) }; root.Children.Add(editor);
        resetRequest = () => { if (selection.SelectedItem is TestCase action && action.Preparation is null) editor.Text = PackageJson.Write(action.Request); };
        bool active = false;
        void SetActive(bool value) { active = value; header.IsEnabled = editor.IsEnabled = preview.IsEnabled = execute.IsEnabled = restore.IsEnabled = !value; }
        Closing += (_, e) => { if (active) e.Cancel = true; };
        void Select()
        {
            if (selection.SelectedItem is not TestCase action) { instructions.Text = "No Guided Actions are defined in this package. Restore remains available for pending preparation."; execute.IsEnabled = preview.IsEnabled = false; return; }
            LoadAttempts(action);
            instructions.Text = action.Instructions;
            identity.SelectedItem = model.Identities.FirstOrDefault(i => i.Alias == action.Request.IdentityRef) ?? model.SelectedIdentity;
            editor.Text = action.Preparation is null ? PackageJson.Write(action.Request) : PackageJson.Write(action.Preparation);
            preview.IsEnabled = action.Preparation is null;
        }
        selection.SelectionChanged += (_, _) => Select(); Select();
        chooseSource.Click += async (_, _) => { try { source = await model.ChooseActionSourceAsync(this) ?? source; SourceLabel(); LoadAttempts(selection.SelectedItem as TestCase); if (selection.SelectedItem is TestCase selected && selected.Preparation is null) editor.Text = PackageJson.Write(selected.Request); } catch (Exception ex) { status.Text = ex.Message; } };
        findResource.Click += async (_, _) =>
        {
            if (selection.SelectedItem is not TestCase action || action.Preparation is not null) return;
            SetActive(true);
            try
            {
                var found = await model.FindGuidedActionSourceAsync(action.Request);
                if (found is null) { status.Text = "No matching response was found in this workspace. Look up the resource ID and enter it manually, or open the workspace where it was created."; return; }
                source = found; SourceLabel(); LoadAttempts(action); editor.Text = PackageJson.Write(action.Request);
                status.Text = "Found saved resource values. Review the selected source run and resolved target before sending. Nothing has been sent.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { SetActive(false); }
        };
        preview.Click += (_, _) => { try { editor.Text = PackageJson.Write(GuidedActionBindings.Resolve(PackageJson.Read<RequestDefinition>(editor.Text), source, chosenAttempts)); } catch (Exception ex) { status.Text = ex.Message + " Enter a verified target manually if the creation response is unavailable."; } };
        execute.Click += async (_, _) =>
        {
            if (selection.SelectedItem is not TestCase action || identity.SelectedItem is not IdentityEditor selectedIdentity) return;
            try
            {
                if (action.Preparation is not null)
                {
                    var plan = PackageJson.Read<PreparationPlan>(editor.Text);
                    var resolvedPlan = await model.ResolvePreparation(plan);
                    var dialog = new PreparationWindow(resolvedPlan, execution: true) { Owner = this };
                    if (dialog.ShowDialog() != true) return;
                    SetActive(true); await model.ApplyGuidedPermissionsAsync(action, dialog.Plan!, selectedIdentity.Alias); status.Text = model.Message;
                }
                else
                {
                    var request = GuidedActionBindings.Resolve(PackageJson.Read<RequestDefinition>(editor.Text), source, chosenAttempts); request.IdentityRef = selectedIdentity.Alias;
                    PackageJson.ValidateRequest(request);
                    editor.Text = PackageJson.Write(request);
                    if (MessageBox.Show(this, $"Run {action.Title}?\n\n{request.Method} {RequestUrls.Resolve(model.BaseUrl, request)}\nIdentity: {selectedIdentity.Alias}\n\nThis can change server data. The request is sent once and recorded separately.", "Run Guided Action", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
                    SetActive(true); status.Text = await model.ExecuteGuidedRequestAsync(action, request, source);
                }
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { SetActive(false); }
        };
        restore.Click += async (_, _) => { SetActive(true); try { await model.RestorePreparation.RunAsync(); status.Text = model.Message; } finally { SetActive(false); } };
        Content = root;
    }
}
