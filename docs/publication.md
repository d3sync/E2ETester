# Publishing safely

Text searches do not inspect screenshot pixels. Every public image or other media file must be visually inspected for private URLs, account names, fixture values, tokens and personal data. Approved bytes are recorded by SHA-256 in `docs/public-media-review.json`. The publication check rejects new or changed media until reviewed; it does not perform OCR or automatically approve images. Do not blindly regenerate the review record.

The documentation screenshots were regenerated from the generic, isolated smoke workspace and visually reviewed. Old images remain in ignored local backups/artifacts and may remain in earlier Git commits until history is explicitly cleaned. Logos and their generated size/icon/wordmark variants were reviewed separately.

Run `python tools/check_publication.py` before staging or publishing. Both release scripts run it too. GitHub CI is currently disabled; run the local checks before pushing. It checks all Git-visible files, including tracked files that now match ignore rules, for excluded file types, private integration markers and common credential formats. It prints paths and reasons, never matched credentials. This is a focused guard, not a guarantee that every possible secret format can be detected. Enable GitHub secret scanning and push protection when creating the repository.

Keep organization packages, fixtures, research and exports in `private/`, or use `*.local.*` / `*.private.*` names. Arbitrary `.jsee` packages are ignored; only reviewed generic examples and the authoring template are allowed. Browser data, encrypted sessions, environment files, databases, logs, build output and release archives are excluded. Synthetic test credentials are intentionally used by local regression tests; Docker test credentials are generated per run.

Public samples use `api.example.test` and an illustrative `/whoami` endpoint. Supply your own API URL and endpoint contract through a local package. Author credits and public documentation/dependency URLs are intentionally retained.

Release staging copies only Git-visible documentation/assets. Do not attach older generated releases: they predate the publication cleanup and can contain private examples. Rebuild releases using the current scripts.

Ignore rules do not remove files already committed. If an actual credential has ever been pushed, rotate it and clean repository history separately. The initial local audit had no `.git` directory, so no commit history or remote repository was available to inspect.
