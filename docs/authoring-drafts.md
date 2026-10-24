# Authoring and local draft recovery

Before the first request, expand **Edit package** at the bottom of the test flow, then **Package details**, to edit the title and description. Use **+ New case**, **Duplicate**, **Delete**, **Move up** and **Move down** to organize the flow. A duplicate gets its own ID and starts with the current edited values. Delete a parent's variations before deleting the parent, to keep references intact.

Once a run starts, its case order and authoring controls are frozen. **+ Variation** still inserts an additional test beside the selected case. Start a **New run** to reorganize the current definitions. Export a package to share the authored tests as JSON.

The application saves a single workspace draft every two seconds while idle, and flushes it again on normal close. **Save draft** saves immediately. On startup the last draft is restored automatically. A crash can lose edits since the last completed save. Disk failures leave the previous saved draft intact and are shown in the status bar.

The draft includes package metadata, saved query definitions, case order, request/check edits, original case values, selected case and the linked run ID. Original values stay separate, so the italic **Original** labels and **Restore** buttons work after restarting. Drafts may contain incomplete definitions; execution/import/export validation still applies when those actions occur.

Drafts retain entered values without redaction. Legacy sensitive metadata no longer changes the saved data. Drafts do not restore browser sessions, manual tokens, connection strings or database fixture selections; remembered identities and connections (see [workspace](workspace.md)) are reloaded separately. Re-enter credentials and reselect fresh fixtures before sending. Unfinished Query Studio SQL and parameters are recovered separately; save the query definition to include it in an exported package.

A run-linked draft restores recorded attempts without sending requests. Finished runs remain closed to execution. If the linked run has changed incompatibly or the draft cannot be read, automatic saving is blocked to preserve the saved data; open the recorded session from **Saved runs**, open a package, or start a new run to proceed.

Opening another package/session replaces the current workspace draft on its next save. Export separate packages if you need multiple independent unfinished drafts. The package library stores independent snapshots. Reordering/deletion protects response dependencies. Versioned autosave history and grouped case operations remain outside the current release.

Run the desktop with `--smoke=artifacts/authoring.png --authoring-exercise` to exercise duplicate/reorder/delete, save/reopen an isolated workspace and verify original-value recovery. The ordinary `--exercise` additionally verifies draft recovery alongside real recorded HTTP attempts.
