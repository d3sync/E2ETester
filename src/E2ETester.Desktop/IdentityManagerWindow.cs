using System.Windows;
using System.Windows.Controls;
namespace E2ETester.Desktop;
public sealed class IdentityManagerWindow : Window
{
    public IdentityManagerWindow(MainViewModel model)
    {
        Title = "Manage identities"; Width = 760; Height = 580; MinWidth = 600; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        model.IncludeReferencedIdentities();
        var root = new DockPanel { Margin = new Thickness(24) };
        var help = new TextBlock { Text = "Add, rename or remove named identities. Changes update saved workspace credentials and remembered sessions for this server. Browser cookies are kept, including after renaming; use Clear saved sign-in data in Swagger to remove them.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) };
        help.Text += " Edit authentication settings in the Auth tab. Use Save environment to keep new identities and tokens between sessions.";
        DockPanel.SetDock(help,Dock.Top); root.Children.Add(help);
        var controls = new StackPanel { Margin = new Thickness(0,16,0,0) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,10) };
        var list = new ListBox { ItemsSource = model.Identities, DisplayMemberPath = "Alias" };
        var name = new TextBox { Margin = new Thickness(0,5,0,10) };
        var replacement = new ComboBox { DisplayMemberPath = "Alias", Margin = new Thickness(0,5,0,10) };
        var buttons = new WrapPanel();
        controls.Children.Add(new TextBlock { Text = "Identity name" }); controls.Children.Add(name);
        controls.Children.Add(new TextBlock { Text = "Replace references with (when deleting an identity used by tests)" }); controls.Children.Add(replacement);
        controls.Children.Add(buttons); controls.Children.Add(status);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_,_) => Close(); controls.Children.Add(close);
        DockPanel.SetDock(controls,Dock.Bottom); root.Children.Add(controls); root.Children.Add(list); Content = root;
        bool busy = false;
        list.SelectionChanged += (_,_) =>
        {
            if(list.SelectedItem is not IdentityEditor current) return;
            name.Text = current.Alias; replacement.ItemsSource = model.Identities.Where(i => i != current).ToList(); replacement.SelectedIndex = -1;
            status.Text = current.IsConfigurable ? $"Used by {model.IdentityUses(current)} test(s). {current.SessionDescription}" : "'none' means no authentication and cannot be renamed or deleted.";
        };
        void Action(string label, Func<Task> action)
        {
            var button = new Button { Content = label }; buttons.Children.Add(button);
            button.Click += async (_,_) =>
            {
                busy = true; buttons.IsEnabled = list.IsEnabled = name.IsEnabled = replacement.IsEnabled = close.IsEnabled = false;
                try { await action(); }
                catch(Exception ex) { status.Text = ex.Message; }
                finally { busy = false; buttons.IsEnabled = list.IsEnabled = name.IsEnabled = replacement.IsEnabled = close.IsEnabled = true; }
            };
        }
        IdentityEditor Selected() => list.SelectedItem as IdentityEditor ?? throw new InvalidOperationException("Choose an identity first.");
        Action("Add", () => { list.SelectedItem = model.CreateIdentity(name.Text); return Task.CompletedTask; });
        Action("Rename", async () => { await model.ChangeIdentityAsync(Selected(), rename:name.Text); status.Text = model.Message; });
        Action("Clear token", async () =>
        {
            var current = Selected();
            if (!current.IsConfigurable) throw new InvalidOperationException("The 'none' identity has no token to clear.");
            if(MessageBox.Show(this,"Clear this identity's token and native login, including saved environments? Browser cookies stay available.","Clear token",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            await model.ChangeIdentityAsync(current,clearToken:true); status.Text = model.Message;
        });
        Action("Delete", async () =>
        {
            var current = Selected();
            if(!current.IsConfigurable)throw new InvalidOperationException("The built-in 'none' identity cannot be deleted.");
            if(model.IdentityUses(current)>0 && replacement.SelectedItem is null)throw new InvalidOperationException("Select a replacement before deleting an identity used by tests.");
            if(MessageBox.Show(this,$"Delete '{current.Alias}' and its saved credentials? {model.IdentityUses(current)} test reference(s) will use your selected replacement. Historical attempts stay unchanged.","Delete identity",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            await model.ChangeIdentityAsync(current,replacement:replacement.SelectedItem as IdentityEditor); list.SelectedItem = model.SelectedIdentity; status.Text = model.Message;
        });
        Closing += (_,e) => { if(busy)e.Cancel = true; };
        list.SelectedItem = model.SelectedIdentity;
    }
}
