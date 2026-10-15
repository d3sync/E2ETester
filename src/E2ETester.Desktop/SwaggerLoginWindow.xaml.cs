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
    private bool ready, closed, reading;
    private readonly Action<string> install;
    private readonly string? browserDataDirectory;
    public bool HasCandidate => candidate is not null;
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public SwaggerLoginWindow(string address, Action<string> install, string profileKey = "default", string? browserDataDirectory = null)
    {
        InitializeComponent(); this.install = install; this.browserDataDirectory = browserDataDirectory; permitted = SwaggerTokenCapture.ValidateAddress(address); Address.Text = address;
        profileName = ProfileName(permitted.GetLeftPart(UriPartial.Authority), profileKey);
        var shortcut = ShortcutPreferences.Load(ShortcutPreferences.DefaultPath)["swagger-use"];
        ReadAndUseToken.ToolTip = "Read Swagger authorization and import it for the selected identity" + (shortcut.Key == System.Windows.Input.Key.None ? "" : " (" + shortcut.Display + ")");
        System.Windows.Automation.AutomationProperties.SetAcceleratorKey(ReadAndUseToken, shortcut.Key == System.Windows.Input.Key.None ? "" : shortcut.Display);
        PreviewKeyDown += async (_, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (shortcut.Key == System.Windows.Input.Key.None || key != shortcut.Key || System.Windows.Input.Keyboard.Modifiers != shortcut.Modifiers) return;
            e.Handled = true;
            if (!e.IsRepeat && ReadAndUseToken.IsEnabled) await ReadAndUseAsync();
        };
        Loaded += InitializeAsync;
        Closed += (_, _) => { closed = true; candidate = null; foreach (var popup in popups.ToArray()) popup.Close(); Browser.Dispose(); };
    }
    internal static string ProfileName(string origin, string key) => "swagger-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(origin + "|" + key)))[..32];
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
            ready = true; ReadToken.IsEnabled = ReadAndUseToken.IsEnabled = true; Browser.CoreWebView2.Navigate(permitted.AbsoluteUri); Ready.TrySetResult();
            Status.Text = "Sign in here. Role identities share this server’s browser cookies and site data; their imported JWTs remain separate. Only Bearer tokens for " + permitted.GetLeftPart(UriPartial.Authority) + " are accepted.";
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
        Status.Text = "Bearer token available for " + permitted.GetLeftPart(UriPartial.Authority) + ". Choose Use token to import it; API permissions are checked when you send.";
    }
    public async Task<string?> ReadAuthorizedAsync()
    {
        if (reading || closed) return null;
        candidate = null; UseToken.IsEnabled = false;
        if (!ready || !Uri.TryCreate(Browser.Source?.AbsoluteUri, UriKind.Absolute, out var page) || page.GetLeftPart(UriPartial.Authority) != permitted.GetLeftPart(UriPartial.Authority)) { Status.Text = "Finish signing in and return to Swagger first."; return null; }
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
        reading = true; ReadToken.IsEnabled = ReadAndUseToken.IsEnabled = false;
        Status.Text = "Reading Swagger authorization…";
        try
        {
            var tokens = JsonSerializer.Deserialize<string[]>(await Browser.ExecuteScriptAsync(script)) ?? [];
            if (closed || permitted.GetLeftPart(UriPartial.Authority) != origin || Browser.Source?.GetLeftPart(UriPartial.Authority) != origin) return null;
            tokens = tokens.Distinct().ToArray();
            if (tokens.Length == 1)
            {
                candidate = null;
                Capture(permitted.AbsoluteUri, "Bearer " + tokens[0]);
                if (candidate is null) Status.Text = "Swagger returned an unusable token. Authorize again, then retry.";
                return candidate;
            }
            else Status.Text = tokens.Length > 1 ? "Multiple authorized tokens found. Make a read-only API request in Swagger to select the token actually used." : "No authorized access token found yet. Use Swagger Authorize, or Try it out on a read-only endpoint to capture its Bearer header.";
        }
        catch { if (!closed) Status.Text = "This Swagger page does not expose its authorization model. Use Try it out on a read-only API request instead."; }
        finally { reading = false; if (!closed) ReadToken.IsEnabled = ReadAndUseToken.IsEnabled = ready; }
        return null;
    }
    internal async Task ReadAndUseAsync()
    {
        var token = await ReadAuthorizedAsync();
        if (token is not null && !closed) ApplyToken(token);
    }
    private async void ReadAndUseAuthorizedToken(object sender, RoutedEventArgs e) => await ReadAndUseAsync();
    private void ApplyToken(string token)
    {
        try { install(token); candidate = null; Close(); }
        catch (Exception ex) { Status.Text = "Could not use token: " + ex.Message; }
    }
    private async void ReadAuthorizedToken(object sender, RoutedEventArgs e) => await ReadAuthorizedAsync();
    private void UseCapturedToken(object sender, RoutedEventArgs e) { if (!reading && candidate is not null) ApplyToken(candidate); }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
