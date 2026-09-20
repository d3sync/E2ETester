# Architecture and proposed file structure

## Project boundaries

Use five production projects initially. Keep provider implementations as folders inside Infrastructure until there is a concrete need to distribute them independently.

```text
E2ETester/
  README.md
  E2ETester.sln
  global.json
  Directory.Build.props
  Directory.Packages.props
  docs/plan/                         # this planning set
  schemas/
    test-package.v1.schema.json
    test-report.v1.schema.json
  samples/
    generic/                        # synthetic, no credentials or live targets
    Example/                       # reviewed scenario templates
  src/
    E2ETester.Core/
      Packages/                     # immutable definitions and validation rules
      Runs/                         # attempts, evidence, lifecycle, outcomes
      Requests/                     # ordered fields, bodies and resolved snapshots
      Authentication/               # profile contracts, session references
      Databases/                    # fixtures, queries, typed results
    E2ETester.Application/
      Abstractions/                 # HTTP, auth, DB, journal, secrets and clock ports
      Packages/                     # import/export and authoring commands
      Runs/                         # coordinator, navigation, resume, variations
      Execution/                    # prepare, execute, assert, capture, verify
      Authentication/               # session selection and refresh coordination
      Reporting/                    # deterministic report assembly
    E2ETester.Infrastructure/
      Http/
      Authentication/Oidc/
      Authentication/ApiTokens/
      Databases/SqlServer/
      Databases/MySql/
      Databases/PostgreSql/
      Databases/Sqlite/
      Persistence/                  # SQLite journal + migrations
      Secrets/                      # Windows user-protected secret store
      Serialization/
      Reporting/                    # JSON and escaped HTML writers
    E2ETester.Desktop/
      Shell/
      Features/                     # Packages, Runner, Auth, DatabaseStudio, Reports
      Controls/                     # field editor, status badge, evidence viewer
      Themes/                       # colors, typography, spacing, accessible states
      Composition/                  # dependency registration
    E2ETester.Mcp/
      Tools/
      Resources/
      Composition/
  tests/
    E2ETester.Core.Tests/
    E2ETester.Application.Tests/
    E2ETester.Infrastructure.Tests/
    E2ETester.Desktop.Tests/
    E2ETester.Mcp.Tests/
    E2ETester.EndToEnd.Tests/
      Fixtures/                     # controlled HTTP/OIDC and database environments
```

Only README and planning documents are created in this phase; the tree above is proposed.

Dependencies: Application -> Core; Infrastructure -> Application/Core; Desktop and MCP compose Application + Infrastructure. Core has no WPF, database-driver or MCP dependency. Both hosts call the same commands, outcome evaluator and report assembler.

## Technology proposals

- C#/.NET 10; WPF/MVVM, native Fluent styling with restrained application resources. Select an MVVM helper only if it removes meaningful boilerplate.
- `HttpClient` transport; an established OIDC client library for browser login rather than handcrafted token validation.
- ADO.NET provider adapters, with candidates Microsoft.Data.SqlClient, MySqlConnector, Npgsql and Microsoft.Data.Sqlite. Review compatible versions and licenses when implementing.
- System.Text.Json plus a JSON Schema validator; SQLite journal, with explicit migrations.
- Official MCP C# SDK if its supported release fits the required transport; pin the negotiated protocol/SDK version during implementation.

WPF is Windows-only. Modern WPF styling is documented in [Microsoft's .NET 10 WPF notes](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100). Native OAuth browser/PKCE design follows [RFC 8252](https://www.rfc-editor.org/info/rfc8252/).

## Execution flow

```text
Package + environment + selected identity + fixture snapshot + user edits
    -> Prepare immutable resolved request
    -> Persist attempt started
    -> Execute HTTP once
    -> Persist response or transport failure
    -> Evaluate assertions / capture outputs
    -> Run requested database checks / collect manual verdicts
    -> Persist evidence and outcome
    -> Navigate or export report
```

Resolve runtime values immediately before execution and freeze them in the attempt. UI edits after Send affect the next draft, never the in-flight attempt. Secret placeholders resolve only in the transport boundary; persisted snapshots are redacted.

## Persistence and process ownership

Workspace metadata and run journal live beneath `%LOCALAPPDATA%/E2ETester/Workspaces/<id>/`; exported packages/reports go to user-selected paths. Store large bodies as bounded, hashed artifact files with journal references.

SQLite is canonical for run state. A requested results JSON is an atomic export/checkpoint of that journal; report-generation failure does not lose attempts. Later evidence is appended with a revision and timestamp rather than overwriting raw execution evidence.

One host owns a run's execution lock at a time, enforced across processes. Desktop can inspect an MCP-owned run read-only; taking control requires an explicit handoff after active work finishes. In-process serialization alone is insufficient. Both processes may read the same journal; do not share plaintext token state over IPC.

## Complexity limits

Sequential execution first; no message broker, microservices, distributed event store or general plugin framework. Use a bounded value resolver and declarative assertion set instead of arbitrary scripting. Add specialized abstraction only when two real implementations need it.
