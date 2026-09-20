# Main plan

Date: 2026-09-19. Status: full-product design baseline. See [implementation status](../implementation-status.md) for delivered increments and remaining work.

## Product goal

Give a tester a prepared JSON test package, guide them through API requests with realistic fixtures, let them change anything and add variations, verify outcomes against responses and databases, and produce evidence that a person or AI agent can assess. Authors can create the same packages inside the application and export them.

The product is local-first, Windows-only at the UI layer, and deliberately simple: one workspace, a three-column testing screen, a sequential run, and a shared execution engine. It should not require a hosted backend or cloud account.

## Planning index

| Document | Responsibility |
| --- | --- |
| [01 Repository findings](01-integration-findings.md) | Verified authentication behavior, source references, integration unknowns |
| [02 Architecture and file structure](02-architecture.md) | Projects, dependencies, persistence, proposed source tree |
| [03 Contracts](03-contracts.md) | Package, run, attempt, assertion, evidence and versioning models |
| [04 Delivery and validation](04-delivery.md) | Build sequence, dependencies, acceptance gates and open decisions |
| [F01 Packages and authoring](features/F01-packages-authoring.md) | Import, edit, validate, export and reusable scenarios |
| [F02 Guided WPF workspace](features/F02-guided-workspace.md) | Three columns, navigation, baseline values and custom tests |
| [F03 HTTP execution](features/F03-http-execution.md) | Methods, headers, parameters, bodies, transport and captures |
| [F04 Authentication](features/F04-authentication.md) | JWT login, API tokens, identity switching and refresh |
| [F05 Fixtures and databases](features/F05-fixtures-databases.md) | Four database engines, fixtures, verification and Query Studio |
| [F06 Outcomes and reports](features/F06-results-reports.md) | PASS/FAIL/INCONCLUSIVE, attempt history and AI-readable reports |
| [F07 MCP](features/F07-mcp.md) | Agent tools using the same engine and persisted run model |
| [F08 Local workspace robustness](features/F08-workspace-robustness.md) | Environments, secrets, recovery, limits and distribution |

## Core user journeys

1. Import a JSON package, select environment, map named identities and database connections, and review prerequisites.
2. Start a run. Select or load fixture rows; the application resolves initial request values and displays the case instructions and expectations.
3. Review or change headers, parameters and body. Send the request, inspect its response, run database checks, and resolve any manual checks.
4. Choose **Next case**, **Run again**, or **Add variation**. A variation appears immediately after its parent case and returns the tester to the original sequence afterward.
5. At the end choose **Add another test** or **Finish and generate report**. Unfinished coverage is visible, never silently converted to a pass.
6. Alternatively, author a package in the application or run the same workflow using MCP.

## Scope and priorities

All requested capabilities belong to the intended product. Delivery is staged, not a reduction of requirements.

- First useful slice: import JSON, token profiles, request editor, sequential wizard, immutable attempt records, assertions and JSON report.
- Next: browser JWT login and identity switching; MySQL fixture selection and database checks for a real Example scenario.
- Then: SQL Server, PostgreSQL and SQLite parity; in-app authoring; Query Studio; HTML report; MCP and packaging.
- Full HTTP request support means standard and custom HTTP method tokens, query/path/header editing, JSON/text/XML/form/multipart/binary bodies and response inspection. WebSocket and gRPC sessions are separate protocols and outside the initial product scope.
- Deferred: team server, cloud synchronization, load testing, arbitrary script plugins, automated test generation, and full database administration.

## Design decisions

- WPF with MVVM; propose .NET 10, subject to implementation-time SDK and dependency verification.
- One application/domain engine shared by the desktop and MCP hosts; no UI automation required for agents.
- JSON is the portable test-definition contract. SQLite is the local durable run journal. JSON and HTML are report exports.
- Baseline case definitions, custom variations and execution attempts have separate identifiers and lifecycles.
- Required fields are selected initially but remain disableable for negative tests. Guidance must not prevent testing invalid requests.
- Authentication belongs to named profiles and is selected per case. A case must never silently execute as a different user.
- Database connections and token values stay in local secret storage; exported definitions contain references.
- No automatic replay of a request after a timeout, lost connection, authentication failure or application crash.

## Completion criteria

A tester can author/export/import a package, authenticate as two users and with an API token, run a fixture-backed sequence, add a variation, verify a result on each supported database provider, resume after interruption, and export a complete report. An MCP client can perform equivalent supported actions with the same outcome rules and evidence format.

## Decisions still requiring integration evidence

These do not block planning. They gate only the affected implementation work:

1. Public Example API-token header names, token types and gateway behavior.
2. Dedicated desktop OAuth client, allowed redirect URI and refresh policy.
3. First real endpoint/scenario and a safe environment with suitable fixture identities.
4. Agreed expected permissions for that scenario, independently of current implementation behavior.
5. Deployment packaging preference and report retention defaults. Proposed defaults appear in F08.
