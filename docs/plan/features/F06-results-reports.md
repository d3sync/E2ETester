# F06 — Results, attempt history and reports

Depends on: durable journal and the outcome rules in [contracts](../03-contracts.md).

## Every attempt is retained

Record request baseline, user changes including enabled toggles, safe identity metadata, timing, response, assertion results, database evidence, errors and manual notes. Start the attempt durably before network execution. UI reruns and agent reruns are equally visible.

Late database evidence or a manual assessment appends a revision. Raw request/response evidence remains immutable. A manual verdict includes who, when and why, and appears beside the computed outcome instead of erasing it.

Show PASS/FAIL/INCONCLUSIVE in text and color. Also show lifecycle/coverage separately: pending, skipped, blocked, cancelled or interrupted. Missing acceptance criteria cannot produce an automatic pass. Expected rejection responses can pass.

## Report output

- JSON: canonical versioned machine-readable report with stable IDs, typed expected/actual values and evidence references.
- HTML: readable summary with case/attempt details, changes, checks and evidence links, generated from the same report model. Escape untrusted content and avoid remote scripts/assets.
- Artifact folder: bounded large bodies/query results with hashes and paths relative to the report directory. Export a manifest; tell the user when sharing requires the whole folder.

Include package version/hash, application version, target build if known, environment alias, gateway/direct target type, identity aliases, start/end times, original/custom coverage, every attempt, retries/reruns, historical failures, incomplete checks and redaction/truncation notices.

Report overview distinguishes the latest effective case result from historical failures. Export checkpoints during a run are marked provisional. Finished reports are immutable snapshots; later evidence produces a new revision.

## AI assessment

An agent should be able to answer: what was tested, under which identity, which values changed, what was expected, what happened, why the verdict was assigned and what evidence is missing. Use stable error categories and explicit references rather than relying on prose parsing. Do not include a speculative AI diagnosis as an observed fact.

## Acceptance

One deliberately mixed run includes pass, fail, inconclusive, skipped and custom cases plus a failed-then-passing rerun. JSON/HTML agree on coverage and outcomes, and expose the earlier failure. Re-export is deterministic apart from declared export metadata. Report failure or a disk-full condition does not silently lose already persisted attempts.
