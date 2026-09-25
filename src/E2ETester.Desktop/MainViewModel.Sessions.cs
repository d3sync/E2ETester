using E2ETester.Core;
namespace E2ETester.Desktop;
public sealed partial class MainViewModel
{
    private readonly RememberedSessions? rememberedSessions;
    private readonly bool useLocalDefaultSuite;
    internal void PruneExpiredIdentities()
    {
        foreach (var identity in Identities.Where(i => i.Secret.Length > 0 && i.ExpiresAt <= DateTimeOffset.UtcNow).ToList())
        {
            identity.Secret = "";
            if (identity.IsSnapshot && IdentityUses(identity) == 0 && identity != SelectedIdentity) Identities.Remove(identity);
        }
        rememberedSessions?.Update(_ => { });
    }
    private void RestoreRememberedSessions()
    {
        if (rememberedSessions is null || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)) return;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        foreach (var identity in Identities.Where(i => i.RememberedOrigin is not null && i.RememberedOrigin != origin))
        { identity.Secret = ""; identity.RememberedOrigin = null; }
        try
        {
            PruneExpiredIdentities();
            foreach (var saved in rememberedSessions.Read().Where(s => s.Origin == origin))
            {
                var identity = Identities.FirstOrDefault(i => i.Alias == saved.Alias) ?? AddIdentity(saved.Alias);
                identity.BrowserProfileKey = saved.BrowserProfileKey;
                if (identity.Secret.Length > 0 || identity.BrowserSession is not null) continue;
                identity.Kind = saved.Kind; identity.Secret = saved.Secret; identity.HeaderName = saved.HeaderName; identity.Prefix = saved.Prefix;
                identity.RememberedOrigin = origin; identity.CapturedAt = saved.CapturedAt ?? DateTimeOffset.UtcNow; identity.ExpiresAt = saved.ExpiresAt; identity.IsSnapshot = saved.IsSnapshot;
            }
            Changed(nameof(SelectedIdentity));
        }
        catch { Message = "Saved sessions could not be read. Sign in again; existing session files have been kept."; }
    }
    internal void PreserveIdentityToken(IdentityEditor identity)
    {
        if (!identity.IsConfigurable || string.IsNullOrWhiteSpace(identity.Secret) || identity.ExpiresAt <= DateTimeOffset.UtcNow) return;
        var origin = RememberedSessions.Origin(BaseUrl);
        if (Identities.Any(i => i != identity && i.IsSnapshot && i.RememberedOrigin == origin && i.Secret == identity.Secret)) return;
        var stem = identity.Alias[..Math.Min(identity.Alias.Length, 48)] + " · " + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var alias = stem; var index = 2;
        while (Identities.Any(i => i.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase))) alias = stem + "-" + index++;
        var snapshot = AddIdentity(alias);
        snapshot.Kind = identity.Kind; snapshot.Secret = identity.Secret; snapshot.HeaderName = identity.HeaderName; snapshot.Prefix = identity.Prefix;
        snapshot.BrowserProfileKey = identity.BrowserProfileKey; snapshot.RememberedOrigin = origin; snapshot.IsSnapshot = true;
        snapshot.CapturedAt = identity.CapturedAt; snapshot.ExpiresAt = identity.ExpiresAt;
        RememberIdentity(snapshot);
    }
    private async Task PreserveBrowserIdentityAsync(IdentityEditor identity)
    {
        if (identity.BrowserSession is not { } browser) { PreserveIdentityToken(identity); return; }
        if (browser.RequiresLogin || browser.Identity.ExpiresAt <= DateTimeOffset.UtcNow) return;
        var current = await browser.GetIdentityAsync(identity.Alias, false);
        var temporary = new IdentityEditor { Alias = identity.Alias, Kind = current.Kind, Secret = current.Secret, BrowserProfileKey = identity.BrowserProfileKey, ExpiresAt = browser.Identity.ExpiresAt };
        PreserveIdentityToken(temporary);
    }
    private void RememberIdentity(IdentityEditor identity)
    {
        if (rememberedSessions is null || !identity.IsConfigurable || identity.BrowserSession is not null) return;
        var origin = RememberedSessions.Origin(BaseUrl);
        rememberedSessions.Update(entries =>
        {
            entries.RemoveAll(s => s.Origin == origin && s.Alias == identity.Alias);
            entries.Add(new(origin, identity.Alias, identity.BrowserProfileKey, identity.Kind, identity.Secret, identity.HeaderName, identity.Prefix, identity.CapturedAt, identity.ExpiresAt, identity.IsSnapshot));
        });
        identity.RememberedOrigin = origin;
    }
}
