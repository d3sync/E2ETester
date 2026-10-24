# Faster testing experience

Date: 2026-10-02. Scope: desktop UX, no JSEE schema change.

Status 2026-10-24 (0.8.6): increments 2, 3 and 4 delivered (single execution bar, Edit package section, collapsible instructions, readiness line with identity/preparation state and the single Auth action); increment 5 partly (attempt count, merged assessment actions, more response space); increment 6 partly (grouped toolbar, remembered view preferences in `UiPreferences.cs`). The readiness logic lives in `MainViewModel.Readiness.cs` over `RunCoordinator.Prerequisites`.

## Goal and evidence

Make the repeated loop **review → send → inspect → next** easy with either mouse or keyboard. Keep the tester's ability to change data, override prerequisites, reuse named identities and recover through Guided Actions.

The current main window has adjacent Send and Next buttons, but also duplicates navigation in the footer. Authoring controls occupy the case list even during execution; setup, utilities and run actions compete in the toolbar. The response and history share limited space. Activity notifications are visible but appear above the work rather than beside the action. These are code/layout findings, not results from a new user study.

## Delivery plan

| Increment | Change | Acceptance criteria |
| --- | --- | --- |
| 1 — Keyboard access (implemented) | Configurable left-hand shortcuts, Tools → Keyboard shortcuts, hover hints and accessibility accelerator metadata. Swagger has a primary Read and use token action; separate read/use remain available. | Works while editing; current grid edits are committed; disabled commands stay disabled; key repeat cannot duplicate sends; no automatic advance or automatic send. |
| 2 — One execution bar | Keep Previous, Prepare (when applicable), Send, Next and Cancel in a consistent request-panel bar. Move run-level report/finish actions into a quieter footer. Remove duplicate Next after user testing. | Send and Next remain adjacent at minimum window size and 150% scaling; button placement does not shift when results arrive; finishing never substitutes for Send. |
| 3 — Focused case view | Default to testing, with authoring actions in an explicit Edit package section. Show a concise objective and expected result; expand full instructions. Collapse empty optional sections, retaining one-click access. | First-time tester can identify what to do next without instruction; parameters, headers and body remain editable; no hidden mandatory setup. |
| 4 — Context and feedback | Show identity alias, token expiry/permission readiness, preparation state and overrides beside execution. Give immediate working/success/failure feedback near the action, with expandable details. | No repeated login for a valid matching identity; notifications are readable/copyable and never cover controls; color is accompanied by text/icon; failed actions explain the next useful action. |
| 5 — Faster inspection and recovery | Let response occupy more space; collapse history by default with attempt count. Case search/filter and “next untested” as separate navigation. Keep Guided Actions accessible beside failures. | Full flow remains selectable; filters do not affect coverage; duplicate-create recovery and manual FAIL take at most two clicks to reach their dialogs. |
| 6 — Setup and polish | Single setup overview for connections, identities and tester bindings, reusing remembered settings. Audit tab order, focus indicators, contrast, dropdowns, resizing and keyboard access in dialogs. | Restart/update preserves data and unfinished position; light/dark and 100/150/200% scaling pass; setup clearly distinguishes missing configuration from optional data. |

Implement and validate each increment separately. Do not turn this into a new wizard that must be completed before ordinary requests can run. Persist display preferences separately from package definitions.

## Keyboard map delivered in increment 1

| Shortcut | Action |
| --- | --- |
| Alt+S | Send request / complete the selected sign-in step; Read and use token in Swagger |
| Alt+D / Alt+A | Next / previous case |
| Alt+W | Prepare selected case |
| Alt+G | Guided Actions |
| Alt+Q | Query Studio |
| Alt+R | View report |
| Alt+X | Cancel current request |
| Alt+B | Sign in with Swagger |
| Ctrl+S | Save local draft |
| Ctrl+O | Open package |
| F1 | Shortcut settings |

Defaults prioritize left-hand operation with the right hand on the mouse. Every action can be reassigned or disabled in Tools → Keyboard shortcuts. Click its field and press a combination, then Save. Clear disables an action; Restore defaults resets the draft settings. Duplicate mappings within a window and standard editing/system combinations are rejected. Workspace and Swagger have separate scopes, so Alt+S can perform their respective primary action. Settings live in `%LOCALAPPDATA%/E2ETester/Settings/shortcuts.json`, outside the installation, and survive updates. Button hints refresh after saving.

These apply to the main workspace, including editors. Modal dialogs retain their own behavior, with the separately configurable Read and use token shortcut in Swagger. Plain Enter remains available for multiline text; Escape remains available to cancel edits/popups. No shortcut for destructive deletion, permission application, Run anyway or Finish run in this increment. Existing button confirmations and command availability still apply. A future dialog-specific pass can add SQL execution after checking focus conflicts.

## Files and responsibilities

- `MainWindow.xaml`: layout, visibility, action hierarchy and menu entry.
- `MainWindow.Shortcuts.cs`: workspace dispatch, edit commit and discoverability.
- `ShortcutPreferences.cs`, `ShortcutSettingsWindow.cs`: defaults, validation, per-user persistence and shortcut editing.
- `SwaggerLoginWindow.xaml` / `.xaml.cs`: primary combined read/use action, retaining separate actions; missing or ambiguous authorization cannot import an older candidate.
- `MainWindow.xaml.cs`: view lifecycle, scrolling, focus and feedback.
- `MainViewModel*.cs`: existing command/state behavior; avoid duplicating execution logic in shortcuts.
- `ThemeControls.xaml`: shared control states, focus and contrast.
- `RequestBodyView.xaml`, `DatabaseWindow.xaml`, `PreparationWindow.cs`: focused dialog/editor improvements.
- `KeyboardShortcutsSmoke.cs`: navigation, editing, repeat suppression and disabled-command regression checks.
- `docs/first-run-study.md`: recruit and run the usability study; append observations and timings without credentials or customer data.

## Measurement and release gate

Use one local reproducible scenario: sign in, create data, switch role, verify permissions, recover a duplicate create with a Guided Action, restore original permissions, export and reopen a report. Run once by mouse and once by keyboard, then ask someone unfamiliar with the app to repeat it. Separate network/login waiting time from UI interaction time.

Record time per step, clicks, navigation backtracks, requests for help, unnecessary re-authentications, accidental repeat requests and successful recovery. Compare the current baseline to each increment. Targets: at least 25% fewer UI interactions, no accidental sends, no lost edits or run history, and every participant able to find failure evidence and restore changes. These are targets, not measured outcomes.

Automated checks supplement the study: pending grid edits included in sends/saves; held keys do not resend; navigation during busy state blocked like its button; dialogs do not trigger background shortcuts; disabled commands remain disabled. Check both themes and narrow/high-DPI layouts. Preserve resumed runs, raw values, paired reports, explicit overrides and stable role identities. Package/report contracts remain unchanged.
