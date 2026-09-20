using System.IO;
using System.Text.Json;
using System.Windows;
using E2ETester.Application;
using Microsoft.Web.WebView2.Core;

namespace E2ETester.Desktop;
public partial class SwaggerLoginWindow : Window
{
    private Uri permitted;
    private string? candidate;
    private bool ready, closed;
    private readonly Action<string> install;
    private readonly string? browserDataDirectory;
    public bool HasCandidate => candidate is not null;
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public SwaggerLoginWindow(string address, Action<string> install, string profileKey = "default", string? browserDataDirectory = null)
    {
        InitializeComponent(); this.install = install; this.browserDataDirectory = browserDataDirectory; permitted = SwaggerTokenCapture.ValidateAddress(address); Address.Text = address;
        profileName = "swagger-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(permitted.GetLeftPart(UriPartial.Authority) + "|" + profileKey)))[..32];
        Loaded += InitializeAsync;
        Closed += (_, _) => { closed = true; candidate = null; foreach (var popup in popups.ToArray()) popup.Close(); Browser.Dispose(); };
    }
    private async void InitializeAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = browserDataDirectory ?? RememberedSessions.BrowserDirectory;
            environment = await CoreWebView2Environment.CreateAsync(userDataFolder: directory);
            if (closed) return;
            var options = environment.CreateCoreWebView2ControllerOptions(); options.IsInPrivateModeEnabled = false; options.ProfileName = profileName;
            await Browser.EnsureCoreWebView2Async(environment, options);
            if (closed) return;
            ConfigureBrowser(Browser.CoreWebView2);
            ready = true; ReadToken.IsEnabled = true; Browser.CoreWebView2.Navigate(permitted.AbsoluteUri); Ready.TrySetResult();
            Status.Text = "Sign in here. This browser saves cookies and site data for this identity. Only Bearer tokens for " + permitted.GetLeftPart(UriPartial.Authority) + " are accepted.";
        }
        catch { Status.Text = "Swagger browser could not start. Install Microsoft Edge WebView2 Runtime, or use the manual token field."; Ready.TrySetException(new InvalidOperationException("Swagger browser initialization failed.")); }
    }
    private void OpenSwagger(object sender, RoutedEventArgs e)
    {
        try { var uri = SwaggerTokenCapture.ValidateAddress(Address.Text); if (!ready) return; permitted = uri; candidate = null; UseToken.IsEnabled = false; Browser.CoreWebView2.Navigate(uri.AbsoluteUri); Status.Text = "Capture scope: " + uri.GetLeftPart(UriPartial.Authority); }
        catch (InvalidDataException ex) { Status.Text = ex.Message; }
    }
    private void Capture(string url, string header)
    {
        var token = SwaggerTokenCapture.FromAuthorization(permitted, url, header);
        if (token is null) return;
        candidate = token; UseToken.IsEnabled = true;
        Status.Text = "Bearer token available for " + permitted.GetLeftPart(UriPartial.Authority) + ". Token is masked. Choose Use token to import it; API permissions are checked when you send.";
    }
    public async Task ReadAuthorizedAsync()
    {
        if (!ready || !Uri.TryCreate(Browser.Source?.AbsoluteUri, UriKind.Absolute, out var page) || page.GetLeftPart(UriPartial.Authority) != permitted.GetLeftPart(UriPartial.Authority)) { Status.Text = "Finish signing in and return to Swagger first."; return; }
        // Read only Swagger's authorization model. Never inspect browser cookies, storage or login forms.
        var origin = permitted.GetLeftPart(UriPartial.Authority);
        var script = """
            (() => {
              if (location.origin !== EXPECTED_ORIGIN) return [];
              const ui = window.ui || window.swaggerUI;
              const state = ui?.authSelectors?.authorized?.();
              const auth = state?.toJS ? state.toJS() : state;
              if (!auth) return [];
              return Object.values(auth).flatMap(a => {
                if (a?.token?.access_token) return [a.token.access_token];
                if (a?.schema?.type === 'http' && a.schema.scheme?.toLowerCase() === 'bearer' && typeof a.value === 'string') return [a.value.replace(/^Bearer\s+/i, '')];
                if (a?.schema?.in === 'header' && a.schema.name?.toLowerCase() === 'authorization' && /^Bearer\s+/i.test(a.value || '')) return [a.value.replace(/^Bearer\s+/i, '')];
                return [];
              });
            })()
            """.Replace("EXPECTED_ORIGIN", JsonSerializer.Serialize(origin));
        try
        {
            var tokens = JsonSerializer.Deserialize<string[]>(await Browser.ExecuteScriptAsync(script)) ?? [];
            if (closed || permitted.GetLeftPart(UriPartial.Authority) != origin || Browser.Source?.GetLeftPart(UriPartial.Authority) != origin) return;
            tokens = tokens.Distinct().ToArray();
            if (tokens.Length == 1) Capture(permitted.AbsoluteUri, "Bearer " + tokens[0]);
            else Status.Text = tokens.Length > 1 ? "Multiple authorized tokens found. Make a read-only API request in Swagger to select the token actually used." : "No authorized access token found yet. Use Swagger Authorize, or Try it out on a read-only endpoint to capture its Bearer header.";
        }
        catch { Status.Text = "This Swagger page does not expose its authorization model. Use Try it out on a read-only API request instead."; }
    }
    private async void ReadAuthorizedToken(object sender, RoutedEventArgs e) => await ReadAuthorizedAsync();
    private void UseCapturedToken(object sender, RoutedEventArgs e) { if (candidate is not null) { install(candidate); candidate = null; Close(); } }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
