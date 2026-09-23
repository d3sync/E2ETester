# Workspace, profiles and distribution

Open **Workspace** in the header for the package library, environments and maintenance.

**Package library** saves redacted snapshots of the current authored package. Each save creates an independent snapshot. Opening one begins a new session. Editing requests, checks, database queries and fixtures uses the existing editor; export JSON to share definitions. Response dependencies must reference earlier cases. Case deletion/reordering detects dependent references.

**Environments** explicitly saves the base URL, named identity settings, manual tokens and database connections. The entire local profile file is protected by Windows DPAPI for the current Windows user. It is neither portable nor included in journal backups or package/report exports. Browser access and refresh tokens are never saved. Loading a profile requires a new run; browser users sign in again. Removing saved settings does not erase an already active in-memory session—use Forget login or close the app.

Workspace drafts include unfinished Query Studio SQL and parameters, body parts and transport settings. Drafts and the library redact configured secrets before persistence; credential values must be restored separately. A redacted binary upload cannot be sent until the file is reselected. Body files are embedded as base64; the file picker caps individual files at 1 MB and package import is capped at 5 MB.

**Maintenance** displays journal size, run count and integrity status. Export diagnostics contains these categories and runtime version, without paths, requests or credentials. Backups use SQLite's online backup API so committed WAL data is included. Choose a new `.db` file. Backups can contain business data even though credentials are redacted.

To restore, close every app/MCP process using the destination workspace. Keep the original workspace intact. Create a new empty workspace directory and copy the backup there as `runs.db`. Use that directory in MCP configuration, or supply `--workspace=<absolute directory>` to the desktop. Existing local profiles can remain in their original workspace; a copied DPAPI file only works for the same Windows user. Never copy an open journal's files as a substitute for the backup command.

**Saved runs → Remove finished** removes only the explicitly selected finished run after a confirmation. Unfinished runs and the run linked to the current draft are protected. Start a new run and save its draft before removing the previous finished run. No automatic retention runs. Deletion is logical removal, not a secure disk erase; existing backups retain their contents.

## Windows release

Run `tools/Publish-Windows.ps1`. It produces a self-contained Windows x64 ZIP under `artifacts/releases`, with separate `desktop` and `mcp` executables, documentation, samples, schemas and a SHA-256 file manifest. Extract the entire directory; run `desktop/E2ETester.Desktop.exe`. An installed .NET runtime is not required. User evidence lives outside the application directory.

The ZIP is unsigned. A signed installer, organization-specific deployment, clean-machine acceptance, and validation against a real identity provider and MFA require the target deployment environment. Hash manifests detect changed files; they do not establish publisher authenticity. Upgrade by extracting a new release into another directory and back up the workspace first. Older app versions cannot be assumed to understand new request fields or reports.

### Single-file publishing

`tools/Publish-Windows.ps1` enables PublishSingleFile for both hosts, bundles native libraries/content for automatic extraction, and omits debug-symbol sidecars. Each host directory must contain exactly its executable or publishing fails. The ZIP also includes optional documentation, schemas and samples. .NET is included; Edge WebView2 Runtime remains a machine prerequisite for Swagger. User workspaces and browser profiles remain outside the executable.

### Closing with edits (0.5.4)

Before closing, the app compares current package/query drafts with the last loaded or explicitly saved state. Unsaved edits offer **Save and exit**, **Cancel**, or **Exit without saving**. Save writes a local redacted draft, not a portable package export. Cancel leaves the window and edits intact. Exit without saving skips the final write; previously created automatic recovery copies may remain. Already journaled attempts are never deleted by this choice. Active API/database operations must finish or be cancelled before closing, and a save failure keeps the app open.

Automatic recovery saves do not acknowledge an explicit save. Save draft, package export and library save do. Passwords/API tokens are not persisted by saving a draft; use an encrypted environment profile for those settings. Reports can be exported later from Saved runs.
