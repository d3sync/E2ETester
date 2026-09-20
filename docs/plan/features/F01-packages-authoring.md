# F01 — Packages and authoring

Depends on: contracts and local workspace persistence.

## Behavior

Import a JSON package into a review screen showing cases, identities, DB requirements and validation errors. Save a working copy; preserve the imported original. No network or database action happens during import.

Create/edit packages and cases in an authoring mode using the same request editor as the runner. Authors enter instructions, defaults, required fields, expected assertions, fixtures, identity references and optional database checks. Duplicate, reorder and delete cases; detect broken capture/dependency references before saving.

Export self-contained definitions with inline query definitions and literal fixture data, or references to environment-local connections. Binary fixture files are optional sidecars with relative paths and hashes; reject paths outside the package root. Explain when an export needs sidecars. Never package credentials or the local secret store.

Promote a run variation into a reusable case explicitly. Existing active runs remain bound to their frozen package revision. Saving a package draft does not rewrite them.

## First implementation

Schema validation, JSON import/export, case CRUD, default parameters/headers/bodies, status/body assertions and logical identity/connection mappings. OpenAPI import is a later convenience; it supplies request shapes, not test expectations or permission rules.

## Acceptance

- Import -> edit -> export -> reimport preserves types, ordered duplicate fields, disabled values and case dependencies.
- Invalid JSON, unsupported schema and dangling references identify the exact path and resolution.
- A package can be shared with another tester who maps local secrets/connections without changing definitions.
- Export never incorporates current JWT/API-token values or credential-bearing URLs.
