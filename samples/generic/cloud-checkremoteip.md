# Cloud sign-in and remote-IP check

This is a generic template, not a real service. Replace `https://api.example.test` and `/whoami` with your local environment contract.

Use E2E Tester 0.7.0 or newer. Double-click cloud-checkremoteip.jsee, open the JSON equivalent, choose Tools → Cloud test, or launch with --auth-check. Loading sends nothing.

1. Sign in with Swagger using cloud-user. Import the token and choose Complete sign-in step. This records token availability without HTTP.
2. Choose Next case and send GET /whoami. Expect 200 and a non-empty string; inspect the actual value. A 404 remains a failure; inspect Request / cURL.
3. Add business cases or variations. Finish and export, then view/copy the readable or JSON report.

Fixtures are not needed. Browser sessions persist subject to Microsoft policies; imported tokens remain separate. Save an encrypted environment explicitly or import a fresh token. Authenticated success needs your account and has not been live-verified here.
