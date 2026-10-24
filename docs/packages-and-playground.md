# Packages, reports and API workspace

## .jsee files

A `.jsee` package is UTF-8 JSON conforming to the [package schema](../schemas): 1.4 for ordinary suites, 1.5 when a case includes [preparation](preparation-and-connections.md). Schema 1.6 adds [Guided Actions](guided-actions.md) for optional cleanup and permission controls outside the sequence. Schema 1.7 adds JSON Schema assertions. Schema 1.8 adds tester claim bindings and case applicability ([tester setup](tester-setup.md)). Older 1.0–1.3 packages still import. Export defaults to .jsee; JSON remains available. Opening a package never executes HTTP or SQL.

Normal startup checks/repairs current-user Windows registration, the icon and quoted open command. Existing Windows UserChoice is respected; use Open with > E2E Tester if Windows selected another app. Velopack registers its stable launcher. After moving a raw executable, run it again to repair the association.

Double-clicked files use separate workspaces based on their full paths. Opening the same file twice is rejected by the workspace lock. The explicit file takes precedence over recovery. Autosave remains local; Export package writes the portable file.

## Suite setup

New suites, imported packages and exports start with Swagger sign-in, then GET `/whoami` (adjust the endpoint to your API's contract). Sign-in has `kind: swaggerAuthentication`; it records bearer-token availability without HTTP. The following case expects 200 and a non-empty string. Business cases require both steps to pass. Repeating sign-in requires fresh validation. Historical runs are not rewritten.

Keep the `cloud-user` identity for both setup steps. Complete Swagger sign-in, import the token, then choose Complete sign-in step. Select Next case and send the IP request. The readiness line above Send request states what is still needed and offers that action; Next case is highlighted once the selected case has been tested. Subsequent cases may use other identities. Fixtures remain optional by default.

## Readable reports

View report opens a modal with Simple report, Full report and Structured JSON tabs. Select text or use Copy entire tab. Export report and Finish run also open this modal after writing the HTML report and ZIP evidence bundle. Attempts and untested cases remain visible; JSON is the complete evidence format. Response text cannot execute script in the viewer.

## API workspace

Tools > API workspace opens a separate window and persistent workspace for ad-hoc HTTP, with the request editor, named identities, cURL evidence, history and recovery, without suite setup steps. Tools > Import OpenAPI request lists operations from a JSON/YAML OpenAPI URL or file, or a Postman 2.1 collection, and adds a selected request. Export package turns these drafts into a guided suite.

Swagger 2.0 and OpenAPI 3.x import accepts JSON/YAML and explicitly supplied reference documents. The same dialog imports Postman 2.1 collections with migration warnings. Choose a request or import all as a package. Review values and unsupported features before sending. See [imports and CI](imports-and-ci.md).

References: [Windows default programs](https://learn.microsoft.com/en-us/windows/win32/shell/default-programs), [OpenAPI specification](https://spec.openapis.org/oas/v3.0.3).

## Private default suite

Place a private package at `%LOCALAPPDATA%\E2ETester\Packages\default-suite.jsee` to use it for new suites and **Tools → Cloud test** on this Windows account. It stays outside the repository and installer. Existing recovered drafts retain their own cases; choose **Tools → Cloud test** to open the private default explicitly. Include the Swagger authentication step followed by the HTTP validation case with ID `remote-ip`. Its endpoint may follow your environment's contract. The Swagger address is resolved from that package's base URL and sign-in step.
