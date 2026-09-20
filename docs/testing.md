# Testing

Run from the repository root on Windows:

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release
dotnet run --project tests/E2ETester.IntegrationTests --configuration Release --no-build
python tools/test_mcp.py
python tools/test_browser_restart.py src/E2ETester.Desktop/bin/Release/net10.0-windows/E2ETester.Desktop.exe
```

The console suite exercises real loopback HTTP, signed synthetic OIDC, SQLite, redaction and Windows profile protection. `Test-Databases.ps1` adds Docker-based provider checks. No live company credentials are needed.

The desktop accepts `--smoke=<png>` and runs in a temporary workspace. It writes screenshots, `.bindings.log`, and `.error.txt` on failure. Useful modes:

| Flag | Coverage |
| --- | --- |
| `--usability-exercise` | Fixture explanation, cloud-package switch, field deletion/recovery, tab hover template and compact Auth |
| `--exit-exercise` | Dirty tracking, save/cancel/exit policy, actual modal cancel/save closing, draft restoration |
| `--swagger-exercise` | Local WebView2 access-token import, popup/redirect/opener/close and browser-session reuse |
| `--workspace-exercise` | Protected profiles, query/body recovery and package library |
| `--exercise`, `--database-exercise` | Guided HTTP/database journeys with the local demo server running |
| `--gateway-exercise` | Explicit live unauthenticated Example GET; omit in ordinary CI |

Add `--dark` to check the dark theme. Browser restart verification launches separate app and Chromium processes and uses synthetic persistent HTTP-only cookies. It does not prove a particular Microsoft tenant will skip MFA.

Validate reports with `tools/validate_report.py` using Python `jsonschema`. Version changes should also test the built single-file executable. Installer/update checks must use isolated folders and a local feed; see [updates](updates.md). A clean-machine installation and public signed release remain separate deployment checks.

To exercise real Velopack replacement and restart, build two test versions into the same local feed, preserving the older portable ZIP before building the newer version. Run `python tools/test_velopack.py <older-portable.zip> <feed-folder> <new-version> <old-version>`. It extracts into a unique temporary folder and refuses a non-portable installation. The check verifies the restarted assembly version after both upgrade and rollback. Test-only version packages must not be published as historical releases.
