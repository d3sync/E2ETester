using E2ETester.Core;
namespace E2ETester.Application;
public static class SuiteDefaults
{
    public static TestPackage Create(string title = "New API test suite", string baseUrl = "https://api.example.test") => Ensure(new TestPackage { Title = title, BaseUrl = baseUrl });
    public static TestPackage Ensure(TestPackage source)
    {
        var package = PackageJson.Clone(source) with { SchemaVersion = "1.4" };
        if (package.Cases.FirstOrDefault(c => c.Kind == TestStepKind.SwaggerAuthentication) is { } existing)
        {
            package.Cases.Remove(existing); package.Cases.Insert(0, existing);
            if (package.Cases.Count > 1 && package.Cases[1].Kind == TestStepKind.Http && (package.Cases[1].Request.Url == "/whoami" || package.Cases[1].Id == "remote-ip" || package.Cases[1].Id.EndsWith("-ip", StringComparison.Ordinal))) return package;
            var validation = Create().Cases[1]; validation.Request.IdentityRef = existing.Request.IdentityRef;
            package.Cases.Insert(1, validation); return package;
        }
        var prefix = "setup-" + Guid.NewGuid().ToString("N")[..8];
        package.Cases.InsertRange(0, [
            new() { Id = prefix + "-auth", Kind = TestStepKind.SwaggerAuthentication, Title = "01 · Sign in with Swagger",
                Instructions = "Sign in through Swagger and import the bearer token. Complete this step without sending an API request. Server access is validated next.",
                Request = new() { Url = "/swagger/", IdentityRef = "cloud-user" } },
            new() { Id = prefix + "-ip", Title = "02 · Validate remote IP", Instructions = "Send GET /whoami using the signed-in identity. Expect HTTP 200 and a non-empty string. Review the returned value.",
                Request = new() { Url = "/whoami", IdentityRef = "cloud-user" }, Assertions = [new(), new() { Kind = CheckKind.BodyNotEmpty, Expected = "" }] }
        ]);
        return package;
    }
}
