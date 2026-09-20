# F02 — Guided WPF workspace

Depends on: package and run commands; request editor and attempt model.

## Layout

```text
Header: Package | Environment | Active identity | Connection state | Run / progress
+---------------------+--------------------------------+----------------------------+
| Case flow           | Current test                   | Evidence                   |
|                     | Instructions + expectations    | Response / Checks / DB     |
| 1. Case             | Method + URL                   | Status, time, size         |
|   1.1 Variation     | Params / Headers / Body / Auth | Response body and headers  |
| 2. Case             | Editable request fields        | Assertions and DB results  |
| Attempts/status     | Send / Cancel / Add variation  | Attempt history / notes    |
+---------------------+--------------------------------+----------------------------+
Footer: Previous | case outcome / remaining checks | Next case | Finish
```

Default proportions approximately 22/43/35, resizable. On narrow windows collapse the case list or evidence into a drawer; do not compress fields until unreadable. Keep primary actions stable while advanced settings use tabs/disclosure. Query Studio opens as a workspace view and can attach results back to the selected attempt.

## Editing and guidance

- Parameters and headers show enabled checkbox, name, value, required badge and source indicator. Required defaults are preselected.
- Disabling a required field marks a deliberate deviation; sending is still allowed for negative testing. Unresolvable bindings and unsupported transport syntax are distinct errors.
- A changed field displays `Original: <resolved value>` immediately underneath in italic muted text with sufficient contrast. Include a restore action. Sensitive originals stay masked.
- Body edits show changed JSON paths and original values in a changes panel; raw text gets a before/after view.
- Every Send creates a new attempt. **Add variation** copies the current draft and expectations into a separately named case instance, records its parent and inserts it next. Prompt the tester to review copied expectations because modified inputs may require different outcomes.
- Navigating back preserves drafts and attempts. Editing after completion does not alter a previous result.
- Next is available after completion; skipping unfinished checks/cases records a reason and incomplete coverage. Dependencies with missing outputs remain blocked.
- Final step offers **Add another test** and **Finish and generate report**. Completion with gaps yields a clearly incomplete report.

## Acceptance

A new tester completes a prepared run, switches identity, creates a variation and exports results using the UI alone. Baseline labels survive reruns; changing a fixture creates an explicit new baseline. Keyboard navigation, screen-reader names, status text alongside colors and 125–200% DPI are verified. Network and database work never block the UI thread.
