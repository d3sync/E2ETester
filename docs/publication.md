# Publishing safely

Run `python tools/check_publication.py` before staging or publishing. CI and both release scripts run it too. It checks all Git-visible files, including tracked files that now match ignore rules, for excluded file types, private integration markers and common credential formats. It prints paths and reasons, never matched credentials. This is a focused guard, not a guarantee that every possible secret format can be detected. Enable GitHub secret scanning and push protection when creating the repository.

Keep organization packages, fixtures, research and exports in `private/`, or use `*.local.*` / `*.private.*` names. Arbitrary `.jsee` packages are ignored; only reviewed generic examples and the authoring template are allowed. Browser data, encrypted sessions, environment files, databases, logs, build output and release archives are excluded. Synthetic test credentials are intentionally used by local regression tests; Docker test credentials are generated per run.

Public samples use `api.example.test` and an illustrative `/whoami` endpoint. Supply your own API URL and endpoint contract through a local package. Author credits and public documentation/dependency URLs are intentionally retained.

Release staging copies only Git-visible documentation/assets. Do not attach older generated releases: they predate the publication cleanup and can contain private examples. Rebuild releases using the current scripts.

Ignore rules do not remove files already committed. If an actual credential has ever been pushed, rotate it and clean repository history separately. The initial local audit had no `.git` directory, so no commit history or remote repository was available to inspect.
