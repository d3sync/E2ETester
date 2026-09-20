using E2ETester.Core;
namespace E2ETester.Desktop;
public sealed partial class MainViewModel
{
    private readonly RememberedSessions? rememberedSessions;
    private void RestoreRememberedSessions()
    {
        if (rememberedSessions is null || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)) return;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        foreach (var identity in Identities.Where(i => i.RememberedOrigin is not null && i.RememberedOrigin != origin))
        { identity.Secret = ""; identity.RememberedOrigin = null; }
        try
        {
            foreach (var saved in rememberedSessions.Read().Where(s => s.Origin == origin))
            {
                var identity = Identities.FirstOrDefault(i => i.Alias == saved.Alias) ?? AddIdentity(saved.Alias);
                identity.BrowserProfileKey = saved.BrowserProfileKey;
                if (identity.Secret.Length > 0 || identity.BrowserSession is not null) continue;
                identity.Kind = saved.Kind; identity.Secret = saved.Secret; identity.HeaderName = saved.HeaderName; identity.Prefix = saved.Prefix;
                identity.RememberedOrigin = origin;
            }
            Changed(nameof(SelectedIdentity));
        }
        catch { Message = "Saved sessions could not be read. Sign in again; existing session files have been kept."; }
    }
    private void RememberIdentity(IdentityEditor identity)
    {
        if (rememberedSessions is null || !identity.IsConfigurable || identity.BrowserSession is not null) return;
        var origin = RememberedSessions.Origin(BaseUrl);
        rememberedSessions.Update(entries =>
        {
            entries.RemoveAll(s => s.Origin == origin && s.Alias == identity.Alias);
            entries.Add(new(origin, identity.Alias, identity.BrowserProfileKey, identity.Kind, identity.Secret, identity.HeaderName, identity.Prefix));
        });
        identity.RememberedOrigin = origin;
    }
}
