# Integration prerequisites

This public repository contains generic API examples and synthetic database fixtures only. Organization-specific research and packages are local, excluded material.

For a real environment, confirm its OpenAPI URL, endpoint contracts, OAuth issuer, public client registration, scopes, redirect URI, API-token header convention and database permissions. Keep those settings in local packages or the encrypted AppData workspace. Do not copy private source code, internal identity headers, credentials or production data into public examples.

Use a dedicated public native OAuth client with authorization code and PKCE when available. Otherwise import a token through the environment's Swagger UI. The caller must supply the API URL; example.test addresses are placeholders.
