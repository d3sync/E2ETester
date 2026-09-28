# Local data, connections and reports

The normal desktop data root is `%LOCALAPPDATA%\E2ETester`, under the signed-in Windows user's profile. Updates replace versioned application files, not `Workspaces`, `Connections`, `Sessions`, `Browser` or user settings. Keep any custom `--workspace` outside the installation's versioned directory. Updates restart into the same workspace.

Workspaces retain the SQLite journal, unfinished draft and selected case, preparation recovery, saved environments, package snapshots and reports. Starting the app normally restores the unfinished draft; it does not replay HTTP calls. Saved runs remain accessible. Do not delete a workspace to update the application. **Tools → Open saved files** opens the current workspace.

On opening a package, the app keeps a local copy in the workspace's `Packages` folder. When executing a run it also stores a run-specific package snapshot and source filename. Reports use that original filename even after restarting. In-app packages without a source file use their title.

**Query Studio → Add/Edit connection → Remember connection and credentials for my Windows user** saves the full connection using Windows DPAPI CurrentUser encryption. It is scoped by API origin and reloaded on startup/package opening. Clear the checkbox and save, or delete the connection, to remove this remembered copy. Existing separately saved environment profiles remain independent; remove those through Workspace if no longer wanted. Package exports contain no connection credentials. Credential-free server defaults continue to work when Remember is off.

**Workspace → Save environment** explicitly saves identity settings/tokens and connections in an encrypted local profile. Browser cookies and valid remembered JWTs also survive restart independently. Encryption is tied to this Windows user and machine; copying protected files to another account is not a supported migration.

**Finish run / Export report** creates two files in the current workspace's `Reports` directory before offering to copy them elsewhere:

- `package-name_reportYYYYMMDD_HHMMSS_fff.html`: simple copyable report, test summary and latest outcome/input/output per case, manual assessments and incomplete coverage. Body previews above 8 KiB are explicitly shortened.
- `package-name_reportYYYYMMDD_HHMMSS_fff_full.zip`: full JSON and HTML evidence, package and SHA-256 manifest. All attempts and complete captured values are retained, subject to the original capture limits.

The report modal offers simple, full and structured views. The journal still retains attempt evidence for recovery/history; compression applies to exported full reports, not the live journal. CLI/MCP canonical JSON and CI outputs remain available for automation.

Drafts, packages and reports contain actual entered/captured values, without redaction. Keep them local or share deliberately. Maintenance offers journal diagnostics and a consistent SQLite backup; that backup excludes encrypted credentials and browser profiles. Preserve those data directories separately when backing up the machine.
