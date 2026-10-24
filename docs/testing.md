# Testing

Run from the repository root on Windows:

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release
dotnet run --project tests/E2ETester.IntegrationTests --configuration Release --no-build
python tools/test_mcp.py
python tools/test_browser_restart.py src/E2ETester.Desktop/bin/Release/net10.0-windows/E2ETester.Desktop.exe
```

The console suite exercises real loopback HTTP, signed synthetic OIDC, SQLite, value retention and Windows profile protection. `Test-Databases.ps1` adds Docker-based provider checks. No real credentials or external services are needed.

The desktop accepts `--smoke=<png>` and runs in a temporary workspace. `--exercise` and `--database-exercise` need the local demo API on port 5188 (`tests/E2ETester.IntegrationTests --serve`, or `Start-Tester.ps1 -Demo`). It writes screenshots, `.bindings.log`, and `.error.txt` on failure. Useful modes:

| Flag | Coverage |
| --- | --- |
| `--features-exercise` | About/notices, reports, identities, updates, preparation, case-list status badges, manual assessments, execution-bar emphasis, remembered view preferences and the readiness line |
| `--authentication-exercise` | Auth configuration failures without contacting an identity provider |
| `--authoring-exercise`, `--assessment-exercise`, `--history-exercise` | Case authoring, manual assessments and saved-run review |
| `--usability-exercise` | Fixture explanation, cloud-package switch, field deletion/recovery, tab hover template, compact Auth and the grouped toolbar |
| `--exit-exercise` | Dirty tracking, save/cancel/exit policy, actual modal cancel/save closing, draft restoration |
| `--swagger-exercise` | Local WebView2 access-token import, popup/redirect/opener/close and browser-session reuse |
| `--workspace-exercise` | Protected profiles, query/body recovery and package library |
| `--exercise`, `--database-exercise` | Guided HTTP/database journeys with the local demo server running |
| `--gateway-exercise` | Sends the loaded sign-in sample's unauthenticated `GET /whoami` and expects 401. Needs a real API (for example a private default suite); omit in CI |

Add `--dark` to check the dark theme. Browser restart verification launches separate app and Chromium processes and uses synthetic persistent HTTP-only cookies. It does not prove a particular Microsoft tenant will skip MFA.

Validate reports with `tools/validate_report.py` using Python `jsonschema`. Version changes should also test the built single-file executable. Installer/update checks must use isolated folders and a local feed; see [updates](updates.md). A clean-machine installation remains a separate deployment check; official releases are signed during publication.

To exercise real Velopack replacement and restart, build two test versions into the same local feed, preserving the older portable ZIP before building the newer version. Run `python tools/test_velopack.py <older-portable.zip> <feed-folder> <new-version> <old-version>`. It extracts into a unique temporary folder and refuses a non-portable installation. The check verifies the restarted assembly version after both upgrade and rollback. Test-only version packages must not be published as historical releases.

`tools/test_cli.py <cli-executable>` checks process exit codes, JUnit, canonical reports and import without HTTP execution. `BusinessJourneyTests` records a disposable HTTP/SQLite workflow and per-step timings under the integration evidence folder. This is not a real-provider or human usability benchmark; see [first-run study](first-run-study.md).
