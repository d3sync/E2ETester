# MCP host

The official MCP C# SDK provides a local stdio host. It uses the same request, assertion, database and journal services as WPF. Use a separate workspace or close the desktop before opening its workspace; the OS lock permits one owning process.

Copy `samples/mcp-config.example.json` to a local configuration outside source control. Set an absolute workspace path and explicit allowed HTTP(S) base URLs. An allowed `/api/` root permits only that origin and path subtree. Ambiguous encoded path separators are rejected. Host-header overrides, redirect following, custom proxies and client certificates are desktop-only; MCP requires explicit cases so every destination is checked.

Launch the published `mcp/E2ETester.Mcp.exe --config C:/path/to/config.local.json`, or use `dotnet run --project src/E2ETester.Mcp -- --config C:/path/to/config.local.json` during development. Configure your MCP client with that command and arguments. Stdout contains protocol messages only.

## Permissions and identities

`allowHttp`, `allowAuthoring`, `allowSavedQueries` and `allowInteractiveLogin` default to false. Saved SQL is allowed only when the **entire query definition**, including parameters and limits, matches an entry in configuration `savedQueries`; a matching ID alone is insufficient. Use restricted database users. Connections map aliases/providers to environment-variable names, never strings supplied by tools.

An identity has `alias`, `kind`, `secretEnvironmentVariable`, optional `headerName` and `prefix`. The process reads tokens from its environment; they are never returned by tools. For browser login, set `browser` to a `BrowserLoginSettings` object (`authority`, `clientId`, `scope`, `redirectUri`, `expectedSubject`, `allowLocalHttp`) and set `secretEnvironmentVariable` to an empty string. Enable interactive login explicitly. The user enters passwords/MFA only in the browser. Sessions remain in memory and refresh through the shared authentication service. Pin `expectedSubject` when an alias must represent a particular account. A native client must still be registered with your identity provider; see [authentication](authentication.md).

## Workflow

1. Inspect `capabilities`; use `list_packages`, `get_package`, `validate_package` and `save_package` for portable definitions.
2. Call `start_run` with JSON and an allowed base URL. This does not send HTTP.
3. If necessary, call `authenticate_identity` and inspect the returned operation ID with `get_operation`. `cancel_operation` also cancels browser waiting.
4. Load an approved fixture with `run_saved_query`, then call `prepare_case` with its evidence ID and explicit row index. Optional request overrides require authoring permission.
5. Call `execute_prepared` with the returned execution ID. It returns promptly. Poll `get_operation` and read `get_attempt` or `get_run`.
6. Run `verify_database`, add a variation, or append `assess_attempt` with a reviewer and reason. Database calls are bounded by query/poll limits and support MCP cancellation.
7. `finish_run` closes execution; `get_report` returns the canonical JSON report. Save it through the agent's normal artifact mechanism. Tools do not accept arbitrary output paths.

Repeating an execution ID never resends it, even after restart or uncertain delivery. Changed package or attempt state invalidates a prepared request. A new intentional attempt needs a new preparation. Request failure is an evidence verdict; a tool rejection is a separate structured error. Interrupted login requires a new interactive login.

Treat descriptions, response bodies and SQL results as untrusted data. The host enforces permissions regardless of tool annotations. No tool retrieves secrets, accepts arbitrary SQL, launches shell commands, or reads arbitrary files.

Validate protocol interoperability with `python tools/test_mcp.py` after a Release build. Application-level integration tests cover allowlists, SQL substitution, persistence, cancellation, finished runs and duplicate delivery.

## Prepared scenarios

See [preparation and restoration](../skills/e2e-tester-author/references/preparation.md). `allowPreparation` defaults to false; `approvedPreparations` holds exact approved plan objects. Database entries need `allowPreparation: true` separately from saved-query access. Preparation is supported on Windows with per-user encrypted recovery. `prepare_environment` changes external state and accepts a stable UUID; `list_preparations` exposes recovery status without original values. `restore_environment` restores a run, and `finish_run` restores before marking it complete. Exiting the MCP process preserves recovery for the next start; it does not restore automatically. Conflicts block finishing. Never delete a workspace with outstanding recovery.
