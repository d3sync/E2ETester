using System.Windows;

namespace E2ETester.Desktop;
internal static class BrowserPersistenceSmoke
{
    public static async Task RunAsync(Window owner, string[] args)
    {
        string Arg(string name) => args.Single(a => a.StartsWith(name + "="))[(name.Length + 1)..];
        var mode = Arg("--browser-persistence");
        var url = Arg("--browser-test-url");
        if (!new Uri(url).IsLoopback) throw new InvalidOperationException("Persistence smoke requires loopback.");
        var sessionDirectory = System.IO.Path.Combine(Arg("--browser-test-data"), "session-test");
        using var model = new MainViewModel(System.IO.Path.Combine(sessionDirectory,mode),sessionDirectory);
        model.LoadNewSuite();
        if (mode == "seed")
        {
            model.SelectedIdentity!.Secret="cross-process-token-canary"; await model.Send.RunAsync();
            await model.ChangeIdentityAsync(model.SelectedIdentity,rename:"persistent-user");
        }
        else
        {
            var identity=model.Identities.Single(i=>i.Alias=="persistent-user");
            if(identity.BrowserProfileKey!="cloud-user" || identity.Secret!=(mode=="verify" ? "cross-process-token-canary" : ""))
                throw new InvalidOperationException("Protected identity did not survive process/executable changes or token clearing.");
            if(mode=="verify") await model.ChangeIdentityAsync(identity,clearToken:true);
        }
        var login = new SwaggerLoginWindow(url, _ => { }, "restart-test", Arg("--browser-test-data")) { Owner = owner, ShowInTaskbar = false };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            login.Show(); await login.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var core = login.Browser.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            for (var i = 0; i < 100; i++)
            {
                if (await core.ExecuteScriptAsync("document.readyState==='complete' && !!window.testPage") == "true") break;
                await Task.Delay(100);
            }
            if (await core.ExecuteScriptAsync("!!window.testPage") != "true") throw new InvalidOperationException("Restart test page did not load.");
            if (!core.Settings.IsPasswordAutosaveEnabled || core.Profile.IsInPrivateModeEnabled) throw new InvalidOperationException("Persistent profile/password prompt settings incorrect.");
            if (mode == "seed") await core.ExecuteScriptAsync("localStorage.setItem('restart-proof','saved')");
            else if (mode == "verify")
            {
                if (await core.ExecuteScriptAsync("window.cookieReceived && localStorage.getItem('restart-proof')==='saved'") != "true") throw new InvalidOperationException("HTTP-only cookie or local storage lost across process restart.");
                await core.Profile.ClearBrowsingDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.AllProfile);
            }
            else if (mode == "cleared")
            {
                if (await core.ExecuteScriptAsync("!window.cookieReceived && localStorage.getItem('restart-proof')===null") != "true") throw new InvalidOperationException("Cleared sign-in data survived restart.");
            }
            else throw new InvalidOperationException("Unknown persistence smoke mode.");
        }
        finally { login.Close(); }
        // Wait for Chromium to exit too, proving disk persistence rather than reuse of an in-memory browser process.
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
