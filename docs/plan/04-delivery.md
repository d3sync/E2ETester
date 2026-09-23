# Delivery plan and validation gates

No dates are promised before the first integration spike. Build stages in order; each should produce a reviewable, useful increment.

| Stage | Deliverables | Acceptance gate |
| --- | --- | --- |
| 0: Contracts and integration spike | Schema examples, outcome rules, public-token contract, proposed native-client registration, chosen pilot scenario | Synthetic package validates; auth unknowns documented with a concrete integration route |
| 1: Core execution | Core/Application projects, HTTP adapter, token profiles, journal, assertions, JSON export | Controlled server proves method/body/header behavior, negative tests and durable attempts |
| 2: WPF guided slice | Three columns, import, navigation, field editing, original-value labels, custom variations, end-of-run choice | Tester completes a package and variation without editing files; interrupted attempt survives restart |
| 3: Real authentication | Browser PKCE login, named sessions, serialized refresh, identity expectations | Two actual identities and a public API token tested in the chosen environment; no silent identity reuse |
| 4: Database-backed pilot | MySQL fixtures, predefined checks, evidence, captures and bounded polling | A real target case verifies both response and DB effects; rejected permission case verifies no forbidden mutation |
| 5: Provider and authoring completeness | SQL Server/PostgreSQL/SQLite, Query Studio, package authoring/export, HTML | Equivalent typed query/check scenarios work on each provider; exported package reimports without semantic loss |
| 6: Agent and release readiness | MCP, process ownership, recovery, packaging, accessibility and retention | Desktop/MCP parity scenario; clean-machine installation; crash/cancel/redaction checks pass |

F07 is designed from stage 0, even though its host ships later. Do not let UI-only business logic accumulate before MCP work begins.

## Meaningful validation

- Domain tests: outcome truth table, skipped coverage, negative responses, required toggles, reference resolution, variations and report selection rules.
- HTTP integration: a controlled local server captures actual requests, repeated query fields, multipart files, binary bodies, redirects, cancellation and ambiguous timeout behavior.
- Auth integration: controlled OIDC service for state/nonce/PKCE, refresh concurrency and rejection; then the real identity provider for custom redirects, MFA and switching. Never substitute mock success for verified real-provider integration.
- Database integration: disposable database per provider with typed parameters, nulls, decimals, UTC/local timestamps, cancellation, limits and read-only permissions. SQLite success does not prove other providers work.
- Durability: terminate between attempt creation, network completion and evidence persistence; recovered state must not claim that the request was never sent or replay it.
- Secret handling: seeded canary secrets must be absent from journal, logs, reports, clipboard defaults and MCP results.
- UI journey: keyboard-only navigation, DPI scaling, resizing, large response rendering, long field values, required-but-disabled fields and visible original values.
- MCP parity: execute the same package through both hosts against identical fixtures; compare normalized outcomes/evidence while ignoring IDs/times.

## Proposed first pilot

Use an approved scenario from the target environment that has documented, resettable test fixtures. Its API + database checks, tenant restrictions and observable side effects exercise the important seams. Do not run existing seed/teardown scripts automatically or reuse their credentials.

Pilot must include a happy path, a rejected cross-client operation, missing required input, a custom variation, and a database check that cannot complete. Expected outcomes must be reviewed against intended business behavior. Record target/package versions because local source and deployment may differ.

## Before implementation of affected features

- Authentication: resolve public API-token header/flow and register the native client.
- Pilot: select the target environment, fixture ownership, test users and exact expectations.
- Packaging: default to a self-contained Windows x64 distribution; decide installer/signing after deployment needs are known.
- Retention: proposed manual cleanup with visible disk usage, no automatic deletion initially.

Planning deliverables are complete when the main plan, source structure, feature plans, acceptance criteria and explicit unknowns are present. They are not a claim that the product or integrations are implemented.
