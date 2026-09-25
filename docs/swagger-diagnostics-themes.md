# Swagger sign-in, request diagnostics and themes

## Import a Swagger access token

The Auth tab includes **Sign in through Swagger / import token**. It opens an embedded Edge WebView2 browser, initially pointed at `https://api.example.test/swagger/`. Sign in yourself and complete Swagger's authorization flow. No desktop client secret is needed for importing a token acquired by Swagger.

Choose **Read authorized token**, then **Use token for selected identity**. If the page does not expose its authorization model, use Swagger's **Try it out** on an appropriate read-only API request. The browser captures its Bearer authorization header, and Use token imports it. Requests you run inside Swagger are real requests; they are not recorded as tester attempts.

Capture accepts only the origin explicitly opened in the address field. Redirects to an authentication provider are permitted for sign-in, but its token requests and ID tokens are not imported. Captured API request evidence includes the actual bearer header. The browser uses a persistent profile per Swagger origin and named identity. Cookies and site storage survive reopening; cookie expiry and server session policies still apply. It does not share Chrome/Edge/Codex profiles, so the first visit requires sign-in. Authorization popups share this profile and retain their opener/callback relationship while following redirects. Back and Reload help recover navigation. Clear saved sign-in data removes browser cookies, site storage and saved passwords for the profile; it does not revoke or clear a previously imported tester token. The embedded browser requires the Microsoft Edge WebView2 Evergreen Runtime. If it is unavailable, the existing manual token input remains usable.

Imported tokens behave like manual bearer credentials: no automatic refresh or verified-identity claim. Import again when expired, or create another named identity to test different permissions. Imported tester tokens stay in memory unless you explicitly save an encrypted local environment profile. Swagger itself may persist its authorization data in the browser profile. Passwords, cookies and browser storage are not read by the token importer.

## Diagnose a sign-in and access check

Choose **Cloud test**, or launch with `--auth-check`, to load the [sign-in and remote-IP sample](../samples/generic/cloud-checkremoteip.md): a Swagger sign-in step followed by `GET /whoami`. Nothing is sent automatically. Replace the placeholder base URL with your own API, import a Swagger token in Auth and send. The 200 expectation is an assumption about your endpoint contract; a 404 or 401 is recorded as a failed expectation, not proof that the token is invalid.

Select **Request / cURL** in the evidence pane. It shows each prepared request: method, fully resolved URL, effective headers (including authentication, cookies and derived content headers), body encoding/bytes, and redirect hops. **Copy first request cURL** copies a Bash/sh command. The command includes captured URL parameters, headers, cookies and body values without redaction. Binary/multipart bodies use base64 with their generated boundary preserved exactly as captured.

This is the request prepared for HttpClient, not a packet capture. HTTP negotiation, proxy framing and other network-layer changes can differ. The recorded transport settings accompany it; copied cURL uses your local TLS/proxy configuration and does not transfer Windows client-certificate settings. A failure before preparing the request, or an older attempt, explicitly shows that no transport snapshot is available. All new snapshots persist in the journal and JSON report (schema 1.5 and later); HTML reports include the captured diagnostic text.

When a request behaves differently from Swagger, compare the recorded URL, method, identity/header name and response headers with the request Swagger sends after signing in.

## Appearance

The header **Dark / Light** button switches GitHub-inspired colors immediately. The preference persists locally for subsequent launches. It applies to the desktop controls and dialogs; external Swagger/login web pages retain their own appearance.

## Verification

The integration suite covers body-byte fidelity, multipart boundaries, redirect snapshots, failed-send evidence, shell quoting, value retention and capture-origin restrictions. `--smoke=artifacts/swagger.png --swagger-exercise` tests a real WebView2 against a local page: ID-token exclusion, authorized access-token reading, Bearer network capture and explicit import, popup redirects/opener callback/closure, and cookie/local-storage retention after reopening. Signing in to a real provider requires user interaction and is not automated.

## Remembering sign-in

Stable browser profiles retain persistent cookies and site storage across full application/browser-process restarts and application upgrades. Reuse the same Swagger origin and named identity. WebView2 now offers its native Save/Update Password prompt on supported username/password forms; saving is the user's choice, and credentials stay in the browser profile rather than the test journal or exported package. General form/address autofill remains disabled.

For Microsoft sign-in, choose **Stay signed in** or **Don't ask again** when the provider offers it. The tester reuses the resulting persistent cookies without forcing a new login in its Swagger browser. It does not convert session-only cookies into permanent cookies, extend expiry, or override Microsoft MFA, revocation, Conditional Access or sign-in-frequency rules. Microsoft may require MFA again even with saved browser data. Browser sign-in and imported API-token lifetime are separate; an expired API token may still need to be imported again.

`python tools/test_browser_restart.py <desktop-executable>` starts three separate app processes and waits for their isolated Chromium processes to exit. It proves HTTP-only persistent-cookie and local-storage retention, verifies password-save prompting is enabled, and proves explicit clearing survives another restart. Actual provider password-prompt rendering and tenant MFA policy remain dependent on the live login page.

References: [WebView2 password prompting](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2profile.ispasswordautosaveenabled), [Microsoft sign-in/MFA session lifetime](https://learn.microsoft.com/en-us/entra/identity/authentication/concepts-azure-multi-factor-authentication-prompts-session-lifetime).
