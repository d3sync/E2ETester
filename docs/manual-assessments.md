# Manual assessments

Select a recorded attempt, then choose **Assess attempt** above its history. Enter a reviewer name, select PASS, FAIL or INCONCLUSIVE, and explain the reason. The assessment is saved to the journal before the UI confirms it. A reviewer and a nonblank reason are required; the limits are 120 and 4,000 characters respectively.

The reviewer is explicitly **self-declared**. It is not an authenticated identity, approval signature or authorization decision. Notes are exported, so avoid secrets and business-sensitive data. Values are retained without redaction; notes are HTML-encoded in the human-readable report.

## Automated and manual results stay separate

An assessment does not replace the attempt's automated verdict, alter assertions, change request/response evidence, or turn missing coverage into PASS. Both verdicts appear in the desktop and reports. The latest assessment of unchanged evidence determines the effective case result, follow-up prerequisites, response bindings and run summary. A manual PASS allows dependent cases to proceed without Run anyway; a manual FAIL takes precedence over an automated PASS. New attempts do not inherit older reviews, and stale assessments do not override the current automated result. `hadFailures` includes historical manual FAIL assessments as well as automated and database failures.

Reassessment appends another record with a stable ID, UTC timestamp, reviewer, verdict, reason, the automated verdict at review time and an evidence hash. All earlier assessments remain available. There is no edit or delete action; add a correction as a new assessment.

The hash covers the recorded request, identity facts, response, checks, fixture and database evidence, excluding manual assessments themselves. A new database verification or other evidence change marks earlier assessments **Evidence changed — review again**. This identifies whether a review still matches the current evidence; it is not a digital signature or tamper-proof audit guarantee.

A new HTTP attempt starts with no manual assessments. Reviews of earlier attempts stay in the full history. Finished runs allow assessment history to be viewed, but reject new records. Running attempts cannot be assessed. If journal saving fails, the in-memory append is rolled back and the reviewer can retry.

## Report 1.3

Each attempt exposes `manualAssessments`. Coverage includes the latest attempt's `manualVerdict`, `manualAssessmentId` and `manualAssessmentIsCurrent`; all three are null when that attempt has no assessment. A stale review retains its verdict and ID but sets `manualAssessmentIsCurrent` to false. JSON and HTML carry the full review history.

The 1.2 report schema remains available for older exports. Existing journal entries load with an empty assessment list; no data migration or HTTP replay occurs.

The integration suite covers append-only history, reviewer/reason validation, distinct automated/manual verdicts, evidence changes, persistence failure, reload, finished-run restrictions, value retention and HTML escaping. With the demo API running, `--smoke=artifacts/assessments.png --assessment-exercise` also exercises the WPF record action and renders the assessment window.
