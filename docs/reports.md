# JSON and HTML reports

Choose **Export report**, then select JSON, HTML or a ZIP evidence bundle in the save dialog. Both formats can be exported during a run or after finishing it. They use the same detached snapshot of the recorded run. Export does not send requests, rerun checks or modify evidence. A failed export leaves the journal available for another export attempt.

JSON reports now use schema **1.7**, defined in [test-report.v1.7.schema.json](../schemas/test-report.v1.7.schema.json). The 1.2 and 1.3 schemas remain available for older exports. Test-package exports use 1.4, including the optional `fixtureRequired` setting. Existing journal records do not require migration; exporting an old run produces the current report format. The application does not yet import report files.

## Reading the result

- `provisional` is true while the run is open. `run.finishedAt` is null until the user finishes it.
- `summary.verdict` reflects the latest attempt per case. Any latest failure means FAIL; missing or incomplete required coverage prevents PASS.
- `summary.hadFailures` includes previous failed attempts, failed database evidence and manual FAIL assessments, even after a later pass. Manual assessments do not override `summary.verdict`.
- `coverage` includes every original and variation case. A never-executed case has `state: "NotRun"`, null `verdict` and null `attemptId`. This is different from an executed INCONCLUSIVE result.
- `run.attempts` retains all attempts, request snapshots, changes, checks, responses, identity facts, fixture selections and database evidence. Truncation and error categories remain explicit.
- `targetBuild` is null because target-build identification is not implemented. Do not infer a deployment version from the report's application version.

Manual review history and freshness are described in [manual assessments](manual-assessments.md). Coverage's manual result sits beside the automated verdict; stale reviews remain explicitly marked.

## Typed checks for AI consumers

`checkEvidence` is a normalized index over the checks in each attempt. Each entry includes the attempt/check IDs, required flag, verdict, message and a JSON Pointer such as `/run/attempts/0/checks/0` back to the recorded evidence.

`expected` and `actual` preserve JSON types for JSON/status/database checks; ordinary text checks remain JSON strings. When a diagnostic such as `Pending` or `No response` cannot be parsed as the expected structured type, the typed value is null. Always retain `expectedText` and `actualText`: these preserve the recorded representation and distinguish a literal JSON null from unavailable structured data. Typed fields are derived views, not new observations.

Lifecycle and verdict are separate: attempt states serialize in camel case (`completed`, `interrupted`, etc.), while coverage states retain the earlier report's `Completed`/`Interrupted`/`NotRun` spelling. Verdicts serialize as `pass`, `fail` or `inconclusive` everywhere in JSON.

## HTML

The HTML file is self-contained and works offline. It includes coverage, chronological attempts, checks, identity provenance, failures and expandable request/response/database evidence. All data is HTML-encoded, with no remote assets or JavaScript. A restrictive content-security policy blocks active content. Expand the evidence sections you want before printing; collapsed sections carry a print reminder.

HTML, text and JSON use the same complete journal values. The legacy `redactions` report field states that values are captured without redaction; its name remains unchanged for schema compatibility. Large response/query bodies remain subject to the execution capture limits. External artifact bundles and signed/hash manifests are not implemented in this version.

## Validation

The integration suite produces a mixed sample report and checks historical failures, incomplete coverage, typed values, evidence pointers, snapshot isolation, deterministic rendering and hostile HTML input. JSON output is deterministic for a fixed `exportedAt`; separate exports intentionally have different timestamps.

For an independent JSON Schema check (Python is a developer tool only, not an application requirement):

```powershell
python -m venv artifacts/report-validation
artifacts/report-validation/Scripts/python.exe -m pip install jsonschema==4.26.0
artifacts/report-validation/Scripts/python.exe tools/validate_report.py path/to/report.json
```

The validator works offline after installation, using the checked-in schema, and also checks evidence-pointer targets. Regenerate the schema after contract changes with `python tools/generate_report_schema.py`.

ZIP bundles include report.json, report.html, package.json and manifest.json. The manifest records each artifact's byte length and SHA-256 hash; it is an integrity check, not a digital signature. Reports record binary encoding, redirect chains, execution IDs and cross-case capture sources.
