using E2ETester.Core;
namespace E2ETester.Desktop;
public sealed partial class MainViewModel
{
    internal void IncludeReferencedIdentities()
    {
        foreach (var alias in Cases.Select(c => c.IdentityRef).Distinct().ToList())
            if (!Identities.Any(i => i.Alias == alias)) AddIdentity(alias);
    }
    private string NextIdentityAlias()
    {
        var index = 1;
        while (Identities.Any(i => i.Alias.Equals($"identity-{index}", StringComparison.OrdinalIgnoreCase))) index++;
        return $"identity-{index}";
    }
    internal IdentityEditor CreateIdentity(string name)
    {
        ValidateIdentityName(name);
        return AddIdentity(name.Trim());
    }
    private void ValidateIdentityName(string name, IdentityEditor? current = null)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl) || name.Equals("none", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a name of 1–80 characters; 'none' is reserved.");
        if (Identities.Any(i => i != current && i.Alias.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An identity already uses that name.");
    }
    internal int IdentityUses(IdentityEditor identity) => Cases.Count(c => c.IdentityRef == identity.Alias);
    internal async Task ChangeIdentityAsync(IdentityEditor identity, string? rename = null, IdentityEditor? replacement = null, bool clearToken = false)
    {
        if (Busy || Database.Busy) throw new InvalidOperationException("Wait for the current operation to finish.");
        if (!identity.IsConfigurable || !Identities.Contains(identity)) throw new InvalidOperationException("The built-in 'none' identity cannot be changed.");
        var old = identity.Alias;
        if (rename is not null) { rename = rename.Trim(); ValidateIdentityName(rename, identity); }
        var deleting = rename is null && !clearToken;
        if (deleting && IdentityUses(identity) > 0)
        {
            if (replacement is null || replacement == identity || !Identities.Contains(replacement)) throw new InvalidOperationException("Choose a replacement for the tests using this identity.");
            if (!replacement.IsConfigurable && Cases.Any(c => c.IdentityRef == old && c.Baseline.Kind == TestStepKind.SwaggerAuthentication)) throw new InvalidOperationException("Sign-in steps need a named replacement identity, not 'none'.");
        }
        var saved = await profiles.LoadAsync<List<SavedEnvironment>>() ?? [];
        if (rename is not null && saved.Any(e => e.Identities.Any(i => i.Alias == old) && e.Identities.Any(i => i.Alias != old && i.Alias.Equals(rename, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("A saved environment already uses that name. Choose another name.");
        var updated = saved.Select(e => e with { Identities = e.Identities.Where(i => !deleting || i.Alias != old).Select(i => i.Alias != old ? i : rename is not null ? i with { Alias = rename } : i with { Secret = "" }).ToList() }).ToList();
        // Only update credentials the user had already explicitly saved; never persist a new token here.
        if (saved.Count > 0) await profiles.SaveAsync(updated);
        if (rememberedSessions is not null)
        {
            var origin = RememberedSessions.Origin(BaseUrl);
            rememberedSessions.Update(entries =>
            {
                if(rename is not null && entries.Any(s=>s.Origin==origin && s.Alias!=old && s.Alias.Equals(rename,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("A remembered identity already uses that name.");
                for(var i=entries.Count-1;i>=0;i--)
                {
                    var entry=entries[i]; if(entry.Origin!=origin || entry.Alias!=old)continue;
                    if(deleting)entries.RemoveAt(i);
                    else entries[i]=rename is not null ? entry with {Alias=rename} : entry with {Secret=""};
                }
            });
        }
        if (rename is not null)
        {
            foreach (var test in Cases.Where(c => c.IdentityRef == old)) test.IdentityRef = rename;
            identity.Rename(rename);
        }
        else
        {
            identity.Secret = ""; identity.RefreshSession();
            if (deleting)
            {
                foreach (var test in Cases.Where(c => c.IdentityRef == old)) test.IdentityRef = replacement!.Alias;
                var wasSelected = SelectedIdentity == identity;
                Identities.Remove(identity);
                if (wasSelected) SelectedIdentity = replacement ?? Identities[0];
            }
        }
        Changed(nameof(SelectedIdentity));
        await SaveDraftAsync();
        Message = rename is not null ? "Identity renamed. Draft references and saved environments updated; previous attempts retain their original identity names." : deleting ? "Identity and its saved credentials removed. Browser cookies are retained; clear them in the Swagger sign-in window if needed." : "Token and native login cleared, including saved environments. Browser cookies are unchanged.";
    }
}
