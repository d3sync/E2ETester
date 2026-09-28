using E2ETester.Core;

namespace E2ETester.Application;

public static class PermissionActions
{
    public static List<TestCase> ForPackage(TestPackage package)
    {
        var actions = PackageJson.Clone(package.GuidedActions ?? []);
        if (package.Cases.FirstOrDefault()?.Kind == TestStepKind.SwaggerAuthentication && package.Cases.Skip(1).FirstOrDefault() is { Kind: TestStepKind.Http } validation && !actions.Any(a => a.Request.Method == validation.Request.Method && a.Request.Url == validation.Request.Url))
            actions.Add(PackageJson.Clone(validation) with { Id = "action-api-access", Title = "Check API access", ParentId = null, Preparation = null, Instructions = "Repeat the suite's access check outside the test flow using the selected identity." });
        var roles = package.Cases.Concat(actions).Where(c => c.Preparation is {} p && PermissionIdentity.IsPermissionPlan(p)).ToList();
        foreach (var role in roles)
        {
            if (actions.Any(a => a.Request.IdentityRef == role.Request.IdentityRef && a.Preparation is {} p && PackageJson.Write(p) == PackageJson.Write(role.Preparation))) continue;
            actions.Add(new TestCase { Id = "permissions-" + role.Id, Title = "Set permissions: " + role.Request.IdentityRef,
                Instructions = "Review and apply this role's permissions. Existing JWTs remain unchanged; sign in for a new token when needed. Use Restore changes to restore the captured original.",
                Request = new RequestDefinition { IdentityRef = role.Request.IdentityRef }, Preparation = PackageJson.Clone(role.Preparation!) });
        }
        foreach (var role in roles.GroupBy(c => PackageJson.Write(c.Preparation! with { ValueJson = "0", Title = "" })).Select(g => g.First()))
        {
            if (actions.Any(a => a.Id == "manual-permissions-" + role.Id || a.Title.Equals("Choose permissions…", StringComparison.OrdinalIgnoreCase))) continue;
            actions.Add(new TestCase { Id = "manual-permissions-" + role.Id, Title = "Choose permissions…",
                Instructions = "Choose any combination of flags or enter a numeric value. Review and apply updates the database even when a matching JWT is already saved. Restore changes restores the captured original.",
                Request = new RequestDefinition { IdentityRef = role.Request.IdentityRef }, Preparation = PackageJson.Clone(role.Preparation!) });
        }
        return actions;
    }
}
