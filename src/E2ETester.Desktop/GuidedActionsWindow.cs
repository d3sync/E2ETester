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
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; footer.Children.Add(status);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var preview = new Button { Content = "Resolve saved values" }; buttons.Children.Add(preview);
        var execute = new Button { Content = "Review and run action…", Style = (Style)FindResource("Primary") }; buttons.Children.Add(execute);
        var restore = new Button { Content = "Restore original permissions / prepared changes" }; buttons.Children.Add(restore);
        var cancel = new Button { Content = "Cancel waiting", Command = model.Cancel }; buttons.Children.Add(cancel);
        var editor = new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 12) }; root.Children.Add(editor);
        bool active = false;
        void SetActive(bool value) { active = value; header.IsEnabled = editor.IsEnabled = preview.IsEnabled = execute.IsEnabled = restore.IsEnabled = !value; }
        Closing += (_, e) => { if (active) e.Cancel = true; };
        void Select()
        {
            if (selection.SelectedItem is not TestCase action) { instructions.Text = "No Guided Actions are defined in this package. Restore remains available for pending preparation."; execute.IsEnabled = preview.IsEnabled = false; return; }
            instructions.Text = action.Instructions;
            identity.SelectedItem = model.Identities.FirstOrDefault(i => i.Alias == action.Request.IdentityRef) ?? model.SelectedIdentity;
            editor.Text = action.Preparation is null ? PackageJson.Write(action.Request) : PackageJson.Write(action.Preparation);
            preview.IsEnabled = action.Preparation is null;
        }
        selection.SelectionChanged += (_, _) => Select(); Select();
        chooseSource.Click += async (_, _) => { try { source = await model.ChooseActionSourceAsync(this) ?? source; SourceLabel(); } catch (Exception ex) { status.Text = ex.Message; } };
        preview.Click += (_, _) => { try { editor.Text = PackageJson.Write(ValueBindings.Request(PackageJson.Read<RequestDefinition>(editor.Text), null, source)); } catch (Exception ex) { status.Text = ex.Message + " Enter a verified target manually if the creation response is unavailable."; } };
        execute.Click += async (_, _) =>
        {
            if (selection.SelectedItem is not TestCase action || identity.SelectedItem is not IdentityEditor selectedIdentity) return;
            try
            {
                if (action.Preparation is not null)
                {
                    var plan = PackageJson.Read<PreparationPlan>(editor.Text);
                    var dialog = new PreparationWindow(plan, execution: true) { Owner = this };
                    if (dialog.ShowDialog() != true) return;
                    SetActive(true); await model.ApplyGuidedPermissionsAsync(action, dialog.Plan!, selectedIdentity.Alias); status.Text = model.Message;
                }
                else
                {
                    var request = ValueBindings.Request(PackageJson.Read<RequestDefinition>(editor.Text), null, source); request.IdentityRef = selectedIdentity.Alias;
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
