using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;

namespace E2ETester.Desktop;
internal static class SwaggerSmoke
{
    public static async Task RunAsync(Window owner, string path)
    {
        var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        var url = $"http://127.0.0.1:{port}/";
        using var server = new HttpListener(); server.Prefixes.Add(url); server.Start();
        int apiRequests = 0;
        bool correctBearer = false;
        var serve = Task.Run(async () =>
        {
            try
            {
                while (server.IsListening)
                {
                    var context = await server.GetContextAsync();
                    if (context.Request.Url!.AbsolutePath == "/whoami")
                    {
                        Interlocked.Increment(ref apiRequests);
                        correctBearer = context.Request.Headers["Authorization"] == "Bearer access-canary-551";
                        var body = Encoding.UTF8.GetBytes("127.0.0.1"); context.Response.ContentType = "text/plain";
                        await context.Response.OutputStream.WriteAsync(body); context.Response.Close(); continue;
                    }
                    if (context.Request.Url!.AbsolutePath == "/redirect") { context.Response.StatusCode = 302; context.Response.RedirectLocation = "/callback"; context.Response.Close(); continue; }
                    if (context.Request.Url.AbsolutePath == "/callback")
                    {
                        var callback = Encoding.UTF8.GetBytes("<script>window.opener.postMessage('oauth-complete',location.origin);window.close();</script>");
                        context.Response.ContentType = "text/html"; await context.Response.OutputStream.WriteAsync(callback); context.Response.Close(); continue;
                    }
                    var bytes = Encoding.UTF8.GetBytes("<html><body><h1>Local Swagger capture check</h1><script>window.ui={authSelectors:{authorized:()=>({oauth:{token:{id_token:'identity-only-canary'}}})}};</script></body></html>");
                    context.Response.ContentType = "text/html"; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        });
        string? imported = null;
        var profileKey = "smoke-" + Guid.NewGuid().ToString("N");
        var login = new SwaggerLoginWindow(url, token => imported = token, profileKey) { Owner = owner, ShowInTaskbar = false };
        try
        {
            login.Show(); await login.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            for (var i = 0; i < 100; i++)
            {
                if (await login.Browser.ExecuteScriptAsync("!!window.ui") == "true") break;
                await Task.Delay(100);
            }
            if (await login.Browser.ExecuteScriptAsync("!!window.ui") != "true") throw new InvalidOperationException("Smoke page failed to load.");
            await login.Browser.ExecuteScriptAsync("window.oauthDone=false;window.addEventListener('message',e=>{if(e.origin===location.origin && e.data==='oauth-complete')window.oauthDone=true});window.authPopup=window.open('about:blank','oauth');window.authPopup.location='/redirect';");
            for (var i = 0; i < 100; i++)
            {
                if (await login.Browser.ExecuteScriptAsync("window.oauthDone===true") == "true" && login.PopupCount == 0) break;
                await Task.Delay(100);
            }
            if (await login.Browser.ExecuteScriptAsync("window.oauthDone===true && !!window.ui") != "true" || login.PopupCount != 0) throw new InvalidOperationException("OAuth popup redirect/opener/close failed.");
            await login.Browser.ExecuteScriptAsync("localStorage.setItem('session-check','retained');document.cookie='session-check=retained;max-age=3600;path=/';");
            await login.ReadAuthorizedAsync();
            if (login.HasCandidate) throw new InvalidOperationException("ID token must not be imported as an access token.");
            await login.Browser.ExecuteScriptAsync("window.ui.authSelectors.authorized=()=>({oauth:{token:{access_token:'access-canary-551'}}})");
            await login.ReadAuthorizedAsync();
            if (!login.HasCandidate || imported is not null) throw new InvalidOperationException("Authorized token capture must require explicit import.");
            await login.Browser.ExecuteScriptAsync("fetch('/api',{headers:{Authorization:'Bearer network-canary-552'}})");
            await Task.Delay(500);
            using (var stream = File.Create(path + ".swagger-page.png")) await login.Browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream);
            login.UseToken.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (imported != "network-canary-552") throw new InvalidOperationException("Network Bearer token capture/import failed.");
            var reopened = new SwaggerLoginWindow(url, _ => { }, profileKey) { Owner = owner, ShowInTaskbar = false };
            try
            {
                reopened.Show(); await reopened.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var retained = false;
                for (var i = 0; i < 100; i++)
                {
                    retained = await reopened.Browser.ExecuteScriptAsync("location.protocol==='http:' && localStorage.getItem('session-check')==='retained' && document.cookie.includes('session-check=retained')") == "true";
                    if (retained) break;
                    await Task.Delay(100);
                }
                if (!retained) throw new InvalidOperationException("Cookies and local storage did not survive browser reopening.");
                await reopened.Browser.CoreWebView2.Profile.ClearBrowsingDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.AllProfile);
            }
            finally { reopened.Close(); }

            if (owner.DataContext is MainViewModel model)
            {
                model.LoadNewSuite(); model.BaseUrl = url.TrimEnd('/'); model.SwaggerUrl = url;
                model.NewIdentity.Execute(null);
                var chosen = model.SelectedIdentity!;
                if (model.Cases[1].IdentityRef != chosen.Alias) throw new InvalidOperationException("Setup validation kept the previous identity.");
                async Task ImportInDialog()
                {
                    var dialog = System.Windows.Application.Current.Windows.OfType<SwaggerLoginWindow>().Single();
                    await dialog.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    for (var i=0;i<100;i++) { if(await dialog.Browser.ExecuteScriptAsync("!!window.ui")=="true")break; await Task.Delay(100); }
                    await dialog.Browser.ExecuteScriptAsync("window.ui.authSelectors.authorized=()=>({oauth:{token:{access_token:'access-canary-551'}}})");
                    await dialog.ReadAuthorizedAsync();
                    dialog.UseToken.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
                Task? interaction = null;
                _ = owner.Dispatcher.BeginInvoke(new Action(()=>interaction=ImportInDialog()));
                await model.OpenSwagger.RunAsync();
                await (interaction ?? throw new InvalidOperationException("Import dialog did not open."));
                if(model.SelectedAttempt?.Verdict != Core.Verdict.Pass || model.SelectedAttempt.IdentityAlias != chosen.Alias || apiRequests != 0)
                    throw new InvalidOperationException("Swagger import did not complete sign-in without HTTP: "+model.Message);
                model.Next.Execute(null);
                if(model.SelectedIdentity != chosen) throw new InvalidOperationException("Next step lost the imported identity.");
                await model.Send.RunAsync();
                if(model.SelectedAttempt?.Verdict != Core.Verdict.Pass || apiRequests != 1 || !correctBearer)
                    throw new InvalidOperationException("Imported token did not reach remote-IP request: "+model.Message);
                await model.Send.RunAsync();
                if(apiRequests != 2 || !correctBearer)throw new InvalidOperationException("Repeated request lost its bearer token.");
                model.LoadNewSuite(); model.BaseUrl = url.TrimEnd('/'); model.SwaggerUrl=url;
                model.SelectedIdentity=model.Identities[0];
                interaction=null;
                _ = owner.Dispatcher.BeginInvoke(new Action(()=>interaction=ImportInDialog()));
                await model.Send.RunAsync();
                await (interaction ?? throw new InvalidOperationException("Send did not open import dialog."));
                if(model.SelectedAttempt?.Verdict != Core.Verdict.Pass || model.SelectedIdentity?.IsConfigurable != true)
                    throw new InvalidOperationException("Send retained the old unauthenticated identity after import: "+model.Message);
            }

        }
        finally { if (login.IsVisible) login.Close(); server.Stop(); await serve; }
    }
}
