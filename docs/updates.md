# Installation, updates and rollback

Velopack 1.2.0 runs before WPF initialization. Applying downloaded updates automatically on startup is disabled; changing version is explicit.

## Build

Run `./tools/Publish-Installer.ps1 -Version <version>`. It restores the pinned `vpk` tool, publishes a self-contained Windows x64 single-file desktop, and creates Setup.exe, a portable bundle, a full package and `releases.win.json` under `artifacts/velopack`. Keep the package ID `E2ETester` stable. Documentation and samples are included. Edge WebView2 Evergreen Runtime remains a machine prerequisite for Swagger.

`Publish-Windows.ps1` still creates the raw desktop/MCP single-file distribution. A raw executable is not itself a Velopack installation; use Setup.exe for managed version changes. The MCP host remains separately distributed.

## Use a feed

The default feed is the maintainer's release feed, `https://e2e.dev.xpk.gr/releases/`. Forks and self-hosted builds should change it here or build with a different default in `UpdatePreferences.cs`. It must serve `releases.win.json` and each referenced `.nupkg`. Open **Updates…** in the main toolbar or **Tools → Updates and settings…**. You can check manually, change the HTTPS/local feed, and toggle **Check for newer versions when the app starts**. Choose **Save update settings** to persist preferences in `%LOCALAPPDATA%/E2ETester/Updates/settings.json`. Existing `update-source.txt` settings are migrated when saved.

Startup checks are enabled by default. A newer version produces a notification and an Updates button showing the available version. Nothing downloads or installs automatically. Offline/unavailable feeds do not block startup; retry manually to see the failure status. If an API operation or modal dialog is active, the button announces the update without interrupting it.

Versions before 0.7.7 do not gain startup checking remotely. Install 0.7.7 once manually, or use their existing **Workspace → Maintenance → Updates / rollback** screen with the feed above. New builds become discoverable only after deployment of the release JSON and packages. Keep older full packages for rollback.

Select a version and choose Install or Roll back. The app confirms the exact version, downloads it, saves the redacted draft, creates a SQLite backup under `%LOCALAPPDATA%/E2ETester/Backups`, and restarts via Velopack. Active API/database operations must finish first. Cancelling a download does not apply it.

Older full packages must remain in the feed. Downgrades explicitly target the selected full package with `UpdateInfo(..., isDowngrade: true)`; they do not rely on “latest” checks. Releases before Velopack integration are not automatically retrofitted into the feed.

## Data

Workspaces, encrypted environments and browser profiles live outside the installation directory. They are not automatically deleted or rolled back. Application rollback is not a database-schema downgrade. Export important evidence before using an older app with fewer supported features; restore a pre-update backup into a separate workspace using a compatible version if needed.

Signing and a production feed URL are maintainer deployment choices. Use a trusted feed: hash verification detects damaged packages but does not independently establish publisher identity. Never reuse a published version number for changed binaries.

Packaging verifies the Velopack bootstrap instead of bypassing it. Update tests use isolated extracted bundles and local feeds. Clean-machine installation and organization-specific distribution policies remain deployment acceptance checks.

References: [WPF integration](https://docs.velopack.io/getting-started/wpf), [specific versions](https://docs.velopack.io/integrating/specific-version), [self-hosting](https://docs.velopack.io/distributing/self-hosting).
