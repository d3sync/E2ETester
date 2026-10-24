# Imports and CI

**Tools → Import OpenAPI request** (also available in the API workspace) opens the shared OpenAPI/Postman importer. Choose a local JSON/YAML file or an HTTP(S) specification URL. Import one request or all requests as a package. Import never runs requests. Export the reviewed package as `.jsee`.

Postman Collection v2.1 imports nested folders in order, collection/folder variables, request methods, URLs, query/header toggles, raw/form bodies and bearer/basic/API-key authentication. Scripts are **not** translated or run, and no assertions are invented. Warnings identify missing variables, unsupported authentication and file/body modes. Resolve `REPLACE_…` values and add checks before running. Imported secrets remain literal values: replace them with identity aliases before sharing the package.

OpenAPI 3.x and Swagger 2.0 accept JSON/YAML, local JSON Pointer references, and split documents supplied with Reference folder. Select the folder containing the referenced JSON/YAML files (maximum 32 documents / 5 MB). Reference keys are relative paths from that folder; place the main specification at its root. Remote references are never fetched. Recursive parameter references are rejected. Complex body generation and serialization styles still need review; this is a request importer, not a complete OpenAPI validator.

## Headless execution

The installer includes `cli/E2ETester.Cli.exe`. From source, substitute `dotnet run --project src/E2ETester.Cli --`.

```powershell
E2ETester.Cli.exe import --input collection.json --output suite.jsee --base-url https://api.example.test
E2ETester.Cli.exe import --input openapi.yaml --references ./specs --output suite.jsee --base-url https://api.example.test
E2ETester.Cli.exe run --package suite.jsee --config host.json --output ./results
```

Example host configuration:

```json
{
  "allowHttp": true,
  "allowedBaseUrls": ["https://api.example.test"],
  "identities": [
    { "alias": "cloud-user", "kind": "bearer", "secretEnvironmentVariable": "E2E_TEST_TOKEN" }
  ]
}
```

Supply `E2E_TEST_TOKEN` through the CI secret environment. Public cases with identity `none` need no identity configuration. Each run retains a journal under the output directory. Do not run two processes against the same output workspace. Use a unique output directory for each CI job. `--base-url` overrides the package default, but cannot bypass the configured target allowlist.

Exit codes: **0** all cases PASS; **1** completed run with FAIL; **2** incomplete, cancelled, invalid configuration or execution error. Upload `junit.xml`, `report.json`, `report.html` and `diagnostics.json` as job artifacts. INCONCLUSIVE is a JUnit error, never a successful skip. Reports retain captured values. Errors before starting a run go to stderr and exit 2.

The runner uses the MCP host configuration model and shared engine. Saved queries must be exactly approved in `savedQueries` and use configured database aliases/environment variables. Required fixtures and fixture-bound requests select row 0; prepare the query to return the intended row. Database verification runs after the HTTP attempt. Preparation needs `allowPreparation`, exact `approvedPreparations` and writable database aliases; it is Windows-only. Restoration is attempted when finishing, including after errors/cancellation. A failed restoration leaves durable recovery and an incomplete run. Process termination cannot guarantee cleanup: inspect/recover the workspace before another run. Requests are never automatically retried.

Headless login never opens a browser or reads desktop sessions. Supply already issued role tokens. A matching valid permission JWT can satisfy preparation without writing to the database. Cases requiring an interactive token refresh cannot complete unattended. Guided Actions are available in the desktop and are not silently executed by the runner.

## JSON Schema assertions

Use package schema **1.7** or newer (each is a superset of the previous). Select `JsonSchema` in Checks and place the schema JSON in Expected; Selector is unused. The entire captured response is validated by JsonSchema.Net. Use an explicit `$schema` for the dialect. Local `$ref` definitions are supported; external references are disabled. `format` follows the library/dialect's annotation behavior; do not assume it asserts email/date syntax. Schema text is limited to 100 KB. A binary, truncated or missing response is INCONCLUSIVE; invalid JSON or a schema mismatch is FAIL.

```json
{
  "kind": "jsonSchema",
  "required": true,
  "expected": "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"required\":[\"id\"],\"properties\":{\"id\":{\"type\":\"integer\",\"minimum\":1}}}"
}
```

Packages without this new assertion can retain their existing schema. Schema 1.7 reports use report schema 1.10.
