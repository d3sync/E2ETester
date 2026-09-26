# First-run study

Status: protocol prepared; an independent human session has not yet been conducted. Automated desktop checks do not replace this study.

Participant: a tester who has never used E2E Tester. Use a fresh Windows account or disposable VM, the release installer and a disposable test service. Do not clear a real user's saved sessions. An observer records timing and assistance without coaching unless the participant is blocked.

## Participant tasks

1. Install and open the app. Find the version and API workspace.
2. Import the supplied collection/specification. Explain what has and has not been imported. Fix its missing parameter and add an expected status check.
3. Open the supplied test package. Sign in, import the token and validate server access.
4. Create an item. Deliberately repeat the create and explain why it failed.
5. Find the cleanup Guided Action, remove the created item and retry the case.
6. Prepare a reader permission set. Authorize in Swagger, import into the reader identity, then run two reader cases. Explain why the second should not need another login.
7. Restore the original permissions. Finish and open the report. Find both the failed duplicate and the latest passing case result.
8. Close and reopen. Identify which identities and browser sessions remain available.

## Observer sheet

| Task | Start/end | Completed unassisted? | Misclicks/backtracks | Help needed / exact wording |
| --- | --- | --- | --- | --- |
| Installation and orientation | | | | |
| Import and configure | | | | |
| Sign-in and validation | | | | |
| Create and duplicate | | | | |
| Cleanup and retry | | | | |
| Role switch and reuse | | | | |
| Restore and report | | | | |
| Reopen | | | | |

Record active task time separately from installer downloads, server latency and MFA waiting. Ask the participant to explain PASS/FAIL/INCONCLUSIVE and what Finish restores. Capture their words. Stop if they target a non-disposable environment accidentally.

Acceptance targets for this initial study: complete every task; no accidental destructive target; no assistance finding Send, Next, identity choice, cleanup or report; correctly explain that an existing JWT retains its permissions after DB restoration. These are proposed targets, not measured results. Turn each observed blocker into a reproducible issue before expanding features.

## Automated companion

The integration suite's `BusinessJourneyTests` runs real loopback HTTP against a disposable SQLite database and synthetic token issuer. It records per-step timings and HTML/JSON evidence under the printed evidence directory's `business-journey` folder. It covers login, create, duplicate failure, cleanup from the earlier response, retry, permission preparation, new role JWT, reuse, restoration and reporting. These timings measure engine/network operations, not human speed or real Microsoft MFA.

Desktop smoke mode creates a fresh isolated workspace and exercises the rendered controls, importer, Swagger popup/cookie handling and named identities. It does not certify installer behavior on a clean Windows VM. Keep these results separate from the human study.
