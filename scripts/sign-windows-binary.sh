#!/usr/bin/env bash
# Sign one PE executable or DLL using the local d3sync self-signed code-signing key.
# Used directly for loose DLLs and by Velopack through --signTemplate.
set -euo pipefail

input="${1:?Usage: sign-windows-binary.sh <PE-file>}"
CODE_SIGN_DIR="${CODE_SIGN_DIR:-/root/.local/share/viridian/codesigning}"
CODE_SIGN_PFX="${CODE_SIGN_PFX:-$CODE_SIGN_DIR/e2etester-d3sync-2026-2029.pfx}"
CODE_SIGN_PASSWORD_FILE="${CODE_SIGN_PASSWORD_FILE:-$CODE_SIGN_DIR/e2etester-d3sync-2026-2029.pfx.password}"
CODE_SIGN_CERT="${CODE_SIGN_CERT:-$CODE_SIGN_DIR/e2etester-d3sync-2026-2029.cert.pem}"
TIMESTAMP_URL="${TIMESTAMP_URL:-http://timestamp.digicert.com}"

command -v osslsigncode >/dev/null 2>&1 || { echo 'Missing required command: osslsigncode' >&2; exit 1; }
[[ -f "$input" ]] || { echo "File does not exist: $input" >&2; exit 1; }
[[ -r "$CODE_SIGN_PFX" && -r "$CODE_SIGN_PASSWORD_FILE" && -r "$CODE_SIGN_CERT" ]] || {
  echo 'Code-signing certificate, password file, or public certificate is unreadable.' >&2
  exit 1
}

output="$(mktemp "${input}.signed.XXXXXX")"
# osslsigncode creates its output and refuses an already-existing mktemp file.
rm -f "$output"
osslsigncode sign -pkcs12 "$CODE_SIGN_PFX" -readpass "$CODE_SIGN_PASSWORD_FILE" \
  -h sha256 -n 'E2E Tester' -i https://d3sync.org -ts "$TIMESTAMP_URL" \
  -in "$input" -out "$output"
chmod --reference="$input" "$output"
mv -f "$output" "$input"
osslsigncode verify -in "$input" -CAfile "$CODE_SIGN_CERT" \
  -TSA-CAfile /etc/ssl/certs/ca-certificates.crt >/dev/null
