# Request bodies, transport and dependencies

Select **Body** and turn on **Send body**. Text mode sends raw JSON, XML or text without fixing intentionally invalid JSON. FormUrlEncoded mode sends enabled name/value parts and preserves repeated names and empty values. Multipart mode sends enabled text/file parts with a generated boundary. Binary mode decodes the body from base64. File pickers load up to 1 MB and embed bytes, never local filesystem paths, in the definition.

Required fields remain editable and can be disabled for negative tests. Sensitive body/part flags redact evidence and known echoes; reselect redacted files before reuse. Binary responses are base64 in JSON with `bodyEncoding: base64`. Text assertions are inconclusive for binary data. **Save captured response** exports complete unredacted captures as bytes or UTF-8 text. Truncated/redacted content cannot be exported through that action.

Optional cookies are kept only in memory, separated by run, origin, identity alias and token value. They are lost on restart. Authentication cookies are redacted from recorded headers and known echoes.

**Transport** (an advanced tab: **Tools → Show advanced tabs**) configures HTTP version preference (fallback allowed and recorded), timeout, custom proxy, local client-certificate thumbprint and redirects. Client certificates come from Current User / Personal and need a valid private key. Normal TLS validation remains mandatory. The default is no redirect following. Optional following is limited to the same origin, records the chain, honors a total timeout, and caps hops. A 303 or POST 301/302 changes the next method to GET; 307/308 retains the method/body. Redirects are recorded network sends, not retries. Raw malformed wire traffic and CONNECT tunneling are outside the .NET request editor.

## Response chaining

Use `{{case.source-id./id}}` to take `/id` from the latest attempt of case `source-id`. Source case IDs appear under Transport. The source must appear earlier in the package and its latest attempt must be completed, PASS, untruncated and without a transport error. A later failing attempt invalidates prior successful values. URL substitutions are escaped; a complete placeholder in a JSON string preserves the actual JSON type. Every consuming attempt records source attempt IDs in `captureSources`.

Database fixtures use `{{fixture.column}}`. Post-request SQL parameters can use `{{response./id}}`; SQL is parameterized rather than interpolated. Bindings never reuse redacted credentials.

## Checks

Status equality; membership (`[200,201]`); header presence/equality; substring; JSON Pointer presence/typed equality/numeric comparisons/array length; and duration less than milliseconds are supported. JSON null is present; a missing path fails. Partial bodies cannot satisfy body checks. Required failing checks mean FAIL, complete required passes mean PASS, and missing/incomplete evidence means INCONCLUSIVE. Optional failures stay visible without failing required coverage.

Database checks add row count, existence/absence, scalar equality/inequality and numeric comparisons. Set Poll seconds to zero for a single observation, or a bounded interval/timeout for eventual consistency. Polling stops at the first pass or incomplete evidence; every observation is retained. It never retries the HTTP request.
