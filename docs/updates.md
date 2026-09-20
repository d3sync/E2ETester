# Installation, updates and rollback

Velopack 1.2.0 runs before WPF initialization. Applying downloaded updates automatically on startup is disabled; changing version is explicit.

## Build

Run `./tools/Publish-Installer.ps1 -Version 0.6.0`. It restores the pinned `vpk` tool, publishes a self-contained Windows x64 single-file desktop, and creates Setup.exe, a portable bundle, a full package and `releases.win.json` under `artifacts/velopack`. Keep the package ID `E2ETester` stable. Documentation and samples are included. Edge WebView2 Evergreen Runtime remains a machine prerequisite for Swagger.

`Publish-Windows.ps1` still creates the raw desktop/MCP single-file distribution. A raw executable is not itself a Velopack installation; use Setup.exe for managed version changes. The MCP host remains separately distributed.

## Use a feed

Host release JSON and referenced `.nupkg` files in an HTTPS directory you control, or use a trusted local folder. In **Workspace → Maintenance → Updates / rollback**, enter the directory address/path and choose **Check versions**. The source is remembered locally, without credentials in its URL. Checks are not automatic.

Select a version and choose Install or Roll back. The app confirms the exact version, downloads it, saves the redacted draft, creates a SQLite backup under `%LOCALAPPDATA%/E2ETester/Backups`, and restarts via Velopack. Active API/database operations must finish first. Cancelling a download does not apply it.

Older full packages must remain in the feed. Downgrades explicitly target the selected full package with `UpdateInfo(..., isDowngrade: true)`; they do not rely on “latest” checks. Releases before Velopack integration are not automatically retrofitted into the feed.

## Data

Workspaces, encrypted environments and browser profiles live outside the installation directory. They are not automatically deleted or rolled back. Application rollback is not a database-schema downgrade. Export important evidence before using an older app with fewer supported features; restore a pre-update backup into a separate workspace using a compatible version if needed.

Signing and a production feed URL are maintainer deployment choices. Use a trusted feed: hash verification detects damaged packages but does not independently establish publisher identity. Never reuse a published version number for changed binaries.

Packaging verifies the Velopack bootstrap instead of bypassing it. Update tests use isolated extracted bundles and local feeds. Clean-machine installation and company distribution policies remain deployment acceptance checks.

References: [WPF integration](https://docs.velopack.io/getting-started/wpf), [specific versions](https://docs.velopack.io/integrating/specific-version), [self-hosting](https://docs.velopack.io/distributing/self-hosting).
