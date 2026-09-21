# Packages, reports and API workspace

## .jsee files

A `.jsee` package is UTF-8 JSON conforming to package schema 1.4. Export defaults to .jsee; JSON remains available. Opening a package never executes HTTP or SQL.

Normal startup checks/repairs current-user Windows registration, the icon and quoted open command. Existing Windows UserChoice is respected; use Open with > E2E Tester if Windows selected another app. Velopack registers its stable launcher. After moving a raw executable, run it again to repair the association.

Double-clicked files use separate workspaces based on their full paths. Opening the same file twice is rejected by the workspace lock. The explicit file takes precedence over recovery. Autosave remains local; Export package writes the portable file.

## Suite setup

New suites, imported packages and exports start with Swagger sign-in, then GET `/whoami`. Sign-in has `kind: swaggerAuthentication`; it records bearer-token availability without HTTP. The following case expects 200 and a non-empty string. Business cases require both steps to pass. Repeating sign-in requires fresh validation. Historical runs are not rewritten.

Keep cloud-user for both setup steps. Complete Swagger sign-in, import the token, then choose Complete sign-in step. Select Next case and send the IP request. Subsequent cases may use other identities. Fixtures remain optional by default.

## Readable reports

View report opens a modal with Readable report and Structured JSON tabs. Select text or use Copy entire tab. Exporting JSON, HTML or ZIP also opens this modal. Attempts and untested cases remain visible; JSON is the complete evidence format. Response text cannot execute script in the viewer.

## API workspace

Tools > API workspace opens a separate window and persistent workspace for ad-hoc HTTP, with the request editor, named identities, cURL evidence, history and recovery, without suite setup steps. Tools > Import OpenAPI request lists operations from a JSON URL or file and adds a selected request. Export package turns these drafts into a guided suite.

Swagger 2.0 and OpenAPI 3.x JSON import supports local parameter references, path/query/header parameters, server URLs, JSON examples and concrete success statuses. Use Swagger's JSON specification URL, not its HTML page. Import does not execute page JavaScript, follow remote references or send operations. Save protected specifications from Swagger and open the local JSON file. YAML, external references, schema-driven body synthesis, complex serialization styles and multiple-server selection are not implemented. Review path values, servers, authentication and bodies before sending.

References: [Windows default programs](https://learn.microsoft.com/en-us/windows/win32/shell/default-programs), [OpenAPI specification](https://spec.openapis.org/oas/v3.0.3).
# Private default suite

Place a private package at `%LOCALAPPDATA%\E2ETester\Packages\default-suite.jsee` to use it for new suites and the Cloud test button on this Windows account. It stays outside the repository and installer. Existing recovered drafts retain their own cases; choose Cloud test to open the private default explicitly. Include the Swagger authentication step followed by the HTTP validation case with ID `remote-ip`. Its endpoint may follow your environment's contract. The Swagger address is resolved from that package's base URL and sign-in step.
