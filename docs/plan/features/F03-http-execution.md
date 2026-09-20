# F03 — HTTP execution and request editing

Depends on: resolved request contract, authentication resolver, journal.

## Capabilities

Support GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS, TRACE and custom valid HTTP method tokens where the .NET transport/server supports them. Explain transport limitations rather than silently translating the request. CONNECT tunneling and raw malformed wire traffic are not ordinary request-editor features.

Support path substitution, ordered repeated query parameters, ordered headers, cookies, content type, JSON, XML, plain text, form URL encoding, multipart fields/files and binary upload/download. Preserve raw text mode for intentionally invalid JSON tests. Handle response status, headers, content, encoding, duration, size and redirects.

Transport settings: timeout, cancellation, proxy, TLS validation, client certificate reference, redirect policy and HTTP-version preference. Default to normal TLS validation and no automatic redirect following for tests; optional following records the chain and never forwards credentials to another origin automatically. Record effective transport settings.

Distinguish content headers from request headers, derived Content-Length from user fields, and transport-controlled headers. Show when the transport cannot honor an entered value. Request preview describes effective semantics; do not claim exact wire bytes unless actually captured.

## Execution rules

1. Resolve bindings, identity and fixtures; show missing prerequisites.
2. Freeze the request/diff and persist an attempt-start event.
3. Send once; user cancellation stops waiting but does not prove the server did not act.
4. Persist response/transport error and evaluate declared assertions.
5. Capture typed response values for dependent cases only after their source is available and meets its configured success policy.

No automatic retry by default, including after token refresh following a 401. The next send is a visible separate attempt. A future opt-in retry policy must record each network send independently.

Use a separate cookie container per environment/identity session. Isolate authenticated transport state between runs where requested. Stream and cap large bodies with explicit truncation/artifact markers. Escape response content when shown as HTML.

## Assertions

Initial operators: status membership, header presence/equality, JSON path existence and typed equality, numeric comparisons, string containment, array count and elapsed-time bound. Add JSON Schema checks once schema support is selected. Avoid arbitrary JavaScript/C# execution in imported packages.

## Acceptance

A controlled server verifies actual methods, duplicate parameters, empty versus omitted values, multipart bytes and cookies. Redirects, cancellation, TLS failure, invalid bodies and timeouts create truthful evidence. An expected error status passes; an unexpected successful status can fail.
