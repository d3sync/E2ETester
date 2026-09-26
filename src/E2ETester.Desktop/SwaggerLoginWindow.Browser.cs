using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace E2ETester.Desktop;
public partial class SwaggerLoginWindow
{
    private readonly string profileName;
    private CoreWebView2Environment? environment;
    private readonly List<Window> popups = [];
    internal int PopupCount => popups.Count;

    private void ConfigureBrowser(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.NewWindowRequested += OpenPopup;
        core.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess && !closed)
                Status.Text = "Browser navigation failed: " + args.WebErrorStatus + ". Use Back or Reload to retry.";
        };
        core.ProcessFailed += (_, args) => { if (!closed) Status.Text = "Browser process stopped: " + args.ProcessFailedKind + ". Reopen the Swagger browser to recover."; };
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (!closed && args.Request.Headers.Contains("Authorization")) Capture(args.Request.Uri, args.Request.Headers.GetHeader("Authorization"));
        };
    }

    private async void OpenPopup(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        args.Handled = true;
        if (closed || environment is null) return;
        var view = new WebView2();
        var popup = new Window { Title = "Swagger authorization", Width = 1000, Height = 760, Owner = this, Content = view, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var popupClosed = false;
        popup.Closed += (_, _) => { popupClosed = true; popups.Remove(popup); view.Dispose(); };
        popups.Add(popup);
        try
        {
            popup.Show();
            var options = environment.CreateCoreWebView2ControllerOptions(); options.ProfileName = profileName;
            await view.EnsureCoreWebView2Async(environment, options);
            if (closed || popupClosed) return;
            ConfigureBrowser(view.CoreWebView2);
            view.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
            // Assign before any navigation: WebView2 preserves window.opener, postMessage and the requested redirect chain.
            args.NewWindow = view.CoreWebView2;
        }
        catch
        {
            if (!popupClosed) popup.Close();
            if (!closed) Status.Text = "The authorization window could not open. Reload Swagger and try Authorize again.";
        }
    }

    private void GoBack(object sender, RoutedEventArgs e) { if (ready && Browser.CoreWebView2.CanGoBack) Browser.CoreWebView2.GoBack(); }
    private void ReloadPage(object sender, RoutedEventArgs e) { if (ready) Browser.CoreWebView2.Reload(); }
    private async void ClearSession(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        try
        {
            foreach (var popup in popups.ToArray()) popup.Close();
            Browser.CoreWebView2.Stop(); candidate = null; UseToken.IsEnabled = false;
            await Browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
            if (closed) return;
            Browser.CoreWebView2.Navigate(permitted.AbsoluteUri);
            Status.Text = "Shared browser cookies, site data and saved passwords cleared for this server. Sign in with the account you want to test. All previously imported identity JWTs are unchanged.";
        }
        catch { if (!closed) Status.Text = "Could not clear the browser session. Close and reopen the browser, then try again."; }
    }
}
