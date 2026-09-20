# Data contracts and state model

This is a proposed contract, not a completed JSON Schema. Implement schemas and round-trip examples in the first delivery stage before binding the UI to them.

## Portable package

| Object | Required meaning |
| --- | --- |
| Package | schemaVersion, packageId, revision, title, description, environment requirements, identities, fixtures, queries, ordered cases |
| Case | stable caseId, title, instructions, endpoint/docs link, preconditions, identityRef, request, assertions, optional DB checks/captures, dependencies |
| Request | method, relative/absolute URL, ordered path/query/header entries, body, timeout and redirect policy |
| Field entry | stable id, name, typed value or binding, enabled, required, sensitive, description |
| Identity definition | logical name, authentication kind and expected identity constraints; no credential values |
| Fixture definition | literal rows or query reference, connectionRef, typed parameters, expected cardinality, selected row/binding |
| Query definition | queryId, provider/dialect, parameterized SQL, connectionRef, parameter types, purpose, read/write designation, timeout/row cap |
| Assertion | assertionId, source/selector, operator, expected typed value, required flag, explanatory message |
| Capture | source attempt/step, selector, destination variable, expected type; unavailable captures block dependents |

Use ordered arrays for headers/query fields so duplicate names survive. Distinguish absent, disabled, empty-string and null. Required is test guidance, not an unbreakable transport constraint. JSON bodies preserve structure for path-level diffs; raw bodies use a text comparison.

Values support literals and explicit bindings. Proposed precedence for named variables is environment < package defaults < run inputs < explicit case inputs, with deliberate user edits last. Fixtures and captures use qualified references (for example `fixture.selected.userId`, `capture.create.userId`) rather than silently shadowing variables. Unresolved, mistyped or cyclic bindings prevent execution with an actionable diagnostic.

The baseline displayed for a field is its resolved value when the case draft is prepared. Retain both the source expression and the resolved baseline. Reloading fixtures creates a new draft baseline revision; it never relabels a historical attempt's original value.

## Run and attempt

- Run: runId, packageId/revision/content hash, frozen definition snapshot, environment metadata, ordered case instances, creation/finish times, coverage, outcome and report revision.
- Case instance: instanceId, caseId, optional parentInstanceId, original/custom marker, position, draft revision and selected attempt.
- Attempt: attemptId, instanceId, sequence, actor human/agent, UTC timestamps, monotonic duration, identity reference and safe identity summary, resolved redacted request, baseline/override diff, response metadata/body reference, transport outcome, assertions, evidence and verdict history.
- Evidence: evidenceId, source HTTP/database/manual, capture time, query/check reference, typed bounded data or artifact reference, truncation and redaction flags, error details and relation to an attempt.
- Manual assessment: actor, timestamp, verdict and reason. Preserve computed outcome separately; manual overrides never erase failed assertions.

## Lifecycle is separate from outcome

Lifecycle: Draft -> Ready -> Running -> AwaitingVerification -> Completed. Also support Blocked, Skipped, Cancelled and Interrupted. Waiting for login is a blocked reason, not an API failure.

Outcome rules:

| Outcome | Rule |
| --- | --- |
| PASS | All required checks completed and passed; at least one defined automated or explicit manual acceptance check exists |
| FAIL | At least one required check definitely contradicted its expectation |
| INCONCLUSIVE | No definitive required failure, but evidence is missing, checks were interrupted/unavailable, or no acceptance checks exist |

Optional check failures are warnings unless the case author marks them required. A 400/401/403 response can PASS when that is the expectation. A timeout is normally INCONCLUSIVE; it can FAIL a specific completed latency/availability assertion. A definite FAIL remains FAIL even if another check is inconclusive.

Unexecuted/skipped cases carry no attempt verdict; coverage records them explicitly. Run outcome: FAIL if any effective required-case outcome fails; otherwise INCONCLUSIVE if required coverage/checks are missing; otherwise PASS. Preserve first/latest/all attempt statistics and report `hadFailures` so reruns cannot hide earlier failures. The default effective attempt is the latest completed attempt, explicitly identified in the report; manual selection requires a recorded reason.

## Report contract

Top level: schemaVersion, reportId/revision, run, application version, target build when known, package hash, environment label/target type, summary, coverage, caseInstances, attempts, evidence, issues, redactions and artifact manifest.

Each issue includes a stable category/code, expected versus actual, check/attempt/evidence references and an explanation. Preserve IDs across JSON and HTML. Never infer successful behavior from HTTP status alone. If target version is unknown, emit null/unknown rather than guessing.

Secrets are redacted before journal writes, logs, exports and MCP responses. Reports declare omitted/truncated evidence; they are diagnostic records, not necessarily byte-for-byte replay bundles.

## Versioning

Reject unsupported major schema versions with a clear message. Define minor-version compatibility explicitly; preserve supported extensions in an `extensions` object. Migrations write a new package/report and retain the original. Validate size, depth, reference integrity and path traversal before import. Do not execute any imported request or SQL automatically.
