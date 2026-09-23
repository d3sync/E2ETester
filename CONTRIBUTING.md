# Contributing

E2E Tester welcomes bug fixes, tests, documentation and focused feature proposals. Contributions are under the repository's MIT license. Follow the [code of conduct](CODE_OF_CONDUCT.md).

## Set up

Use Windows, .NET SDK 10.0.302 or a compatible patch, and Edge WebView2 Runtime for browser sign-in. Python 3 is used for protocol/schema validation. Docker is optional for the four-provider database suite.

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release
dotnet run --project tests/E2ETester.IntegrationTests --configuration Release --no-build
python tools/test_mcp.py
./Start-Tester.ps1 -Demo
```

The tests use synthetic HTTP/OIDC services and SQLite; they do not require real credentials. `Test-Databases.ps1` runs the additional provider tests with isolated infrastructure. See [testing](docs/testing.md).

## Make a change

1. Read the [architecture](docs/architecture.md) and relevant workflow guide.
2. Keep domain contracts in Core, use-case logic in Application, external adapters in Infrastructure, and presentation in Desktop/Mcp.
3. Preserve original requests, historical attempts, redaction and report compatibility. Do not silently retry API writes or change a failed attempt into a passing historical result.
4. Add focused tests for behavior changes. Check both themes for UI changes and inspect binding logs.
5. Update the schemas, samples and docs when changing a public contract. Commit package lockfiles alongside dependency changes.
6. Describe the problem, resulting behavior and validation in your pull request.

Do not commit browser profiles, API tokens, database files, real response bodies or private fixtures. Use synthetic reproductions. For a security problem, follow [SECURITY.md](SECURITY.md).

## Releases

`tools/Publish-Windows.ps1` creates single-file executables and a distribution ZIP. `tools/Publish-Installer.ps1` creates Velopack setup/update assets. The CLI is pinned in `dotnet-tools.json`. Do not change a published version's contents: increment the version. Publishing to a public feed and signing require maintainer-controlled infrastructure; neither happens automatically in CI.
# Publication checks

Before staging changes, run `python tools/check_publication.py`. Keep private test packages and fixtures out of the public repository. See [publication guidance](docs/publication.md).
