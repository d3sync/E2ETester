#!/usr/bin/env bash
set -euo pipefail

# Build and deploy the Windows framework-dependent, single-file E2E Tester
# Velopack release feed served at https://e2e.dev.xpk.gr/releases.
# Usage: scripts/release-static-feed.sh [version]

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SITE_ROOT="${SITE_ROOT:-/srv/e2etester}"
FEED_URL="${FEED_URL:-https://e2e.dev.xpk.gr/releases}"
LANDING_URL="${LANDING_URL:-https://e2e.dev.xpk.gr/}"
VERSION="${1:-}"
DEFAULT_VERSION="$(python3 - "$REPO_ROOT/Directory.Build.props" <<'PY'
import re, sys
text = open(sys.argv[1], encoding='utf-8').read()
m = re.search(r'<Version>([^<]+)</Version>', text)
if not m:
    raise SystemExit('No <Version> found in Directory.Build.props')
print(m.group(1))
PY
)"
CHANNEL="win"
PACK_ID="E2ETester"
APP_CSPROJ="src/E2ETester.Desktop/E2ETester.Desktop.csproj"
TEST_CSPROJ="tests/E2ETester.IntegrationTests/E2ETester.IntegrationTests.csproj"
SOLUTION="E2ETester.slnx"
ICON="assets/branding/e2e-tester.ico"
CODE_SIGN_DIR="${CODE_SIGN_DIR:-/root/.local/share/viridian/codesigning}"
CODE_SIGN_PFX="${CODE_SIGN_PFX:-$CODE_SIGN_DIR/e2etester-d3sync-2026-2029.pfx}"
CODE_SIGN_PASSWORD_FILE="${CODE_SIGN_PASSWORD_FILE:-$CODE_SIGN_DIR/e2etester-d3sync-2026-2029.pfx.password}"
CODE_SIGN_CERT="${CODE_SIGN_CERT:-$CODE_SIGN_DIR/e2etester-d3sync-2026-2029.cert.pem}"
TIMESTAMP_URL="${TIMESTAMP_URL:-http://timestamp.digicert.com}"

need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing required command: $1" >&2; exit 1; }; }
need dotnet; need git; need curl; need python3; need osslsigncode
[[ -r "$CODE_SIGN_PFX" && -r "$CODE_SIGN_PASSWORD_FILE" && -r "$CODE_SIGN_CERT" ]] || {
  echo 'Code-signing certificate, password file, or public certificate is unreadable.' >&2
  exit 1
}

sign_windows_file() {
  local input="$1" output
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
}

sign_windows_tree() {
  local directory="$1" count=0 file
  while IFS= read -r -d '' file; do
    sign_windows_file "$file"
    count=$((count + 1))
  done < <(find "$directory" -type f \( -iname '*.exe' -o -iname '*.dll' \) -print0 | sort -z)
  (( count > 0 )) || { echo "No Windows executables or DLLs found in $directory" >&2; exit 1; }
  echo "Signed and RFC 3161-timestamped $count Windows binary/binaries in $directory"
}
export PATH="$PATH:$HOME/.dotnet/tools"
if ! command -v vpk >/dev/null 2>&1; then
  dotnet tool install --global vpk
  export PATH="$PATH:$HOME/.dotnet/tools"
fi
need vpk

cd "$REPO_ROOT"
git fetch origin --prune
branch="$(git branch --show-current)"
[[ -n "$branch" ]] || { echo 'Detached HEAD; checkout master/main before releasing.' >&2; exit 1; }
[[ -z "$(git status --porcelain --untracked-files=no)" ]] || { echo 'Tracked working tree changes exist; commit/stash before release.' >&2; git status --short; exit 1; }
git pull --ff-only origin "$branch"

if [[ -z "$VERSION" ]]; then
  VERSION="$(python3 - "$SITE_ROOT/releases/releases.win.json" "$DEFAULT_VERSION" <<'PY'
from pathlib import Path
import json, re, sys
feed, fallback = Path(sys.argv[1]), sys.argv[2]
if not feed.exists():
    print(fallback); raise SystemExit
assets = json.loads(feed.read_text(encoding='utf-8-sig')).get('Assets', [])
versions = [a.get('Version', '') for a in assets if a.get('Type') == 'Full']
def key(v): return tuple(int(x) for x in re.findall(r'\d+', v)[:3])
if not versions:
    print(fallback)
else:
    major, minor, patch = key(max(versions, key=key))
    print(f'{major}.{minor}.{patch + 1}')
PY
)"
fi
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][A-Za-z0-9.]+)?$ ]] || { echo "Version must look like 1.2.3, got: $VERSION" >&2; exit 1; }

echo "==> Releasing E2E Tester $VERSION from $(git rev-parse --short HEAD)"
# Remove only generated assets for this requested version before the publication audit,
# so a failed packaging attempt can be safely retried without scanning stale output.
rm -rf Releases "publish/win-$VERSION"
python3 tools/check_publication.py
dotnet build "$SOLUTION" -c Release -p:EnableWindowsTargeting=true
dotnet run --project "$TEST_CSPROJ" -c Release --no-build -p:EnableWindowsTargeting=true

mkdir -p Releases
if [[ -d "$SITE_ROOT/releases" ]]; then
  cp -f "$SITE_ROOT/releases"/E2ETester-*.nupkg Releases/ 2>/dev/null || true
  cp -f "$SITE_ROOT/releases"/RELEASES "$SITE_ROOT/releases"/assets.win.json "$SITE_ROOT/releases"/releases.win.json Releases/ 2>/dev/null || true
fi

dotnet publish "$APP_CSPROJ" -c Release -r win-x64 --no-self-contained -p:PackagingRestore=true \
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true \
  -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -p:Version="$VERSION" \
  -o "publish/win-$VERSION"

# .NET 10 embeds the runtime configuration and all application dependencies in the
# framework-dependent single executable. The target machine supplies only .NET 10.
[[ -f "publish/win-$VERSION/E2ETester.Desktop.exe" ]] || { echo 'Missing desktop executable.' >&2; exit 1; }
if [[ "$(find "publish/win-$VERSION" -maxdepth 1 -type f | wc -l)" -ne 1 ]]; then
  echo 'Unexpected loose files: publish must remain a single framework-dependent executable.' >&2
  find "publish/win-$VERSION" -maxdepth 1 -type f -printf '%f\n' >&2
  exit 1
fi

notes="$(mktemp)"
python3 tools/check_publication.py --stage "publish/win-$VERSION"
dotnet publish src/E2ETester.Mcp/E2ETester.Mcp.csproj -c Release -r win-x64 --no-self-contained -p:PackagingRestore=true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true \
  -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -p:Version="$VERSION" \
  -o "publish/win-$VERSION/mcp"

# Sign every distributable PE binary before it is embedded into the Velopack package.
sign_windows_tree "publish/win-$VERSION"
printf '# E2E Tester %s\n\nStatic-feed release built from commit %s. Requires the .NET 10 Desktop Runtime.\n' "$VERSION" "$(git rev-parse --short HEAD)" > "$notes"
vpk '[win]' pack --packId "$PACK_ID" --packVersion "$VERSION" \
  --packDir "publish/win-$VERSION" --mainExe E2ETester.Desktop.exe \
  --packTitle 'E2E Tester' --packAuthors 'd3sync and E2E Tester contributors' \
  --releaseNotes "$notes" --runtime win-x64 --channel "$CHANNEL" --delta None \
  --icon "$ICON" --outputDir Releases --yes

# The bootstrap installer is produced by Velopack after package assembly, so sign it separately.
sign_windows_file "Releases/E2ETester-win-Setup.exe"
echo 'Signed and RFC 3161-timestamped Velopack setup installer'

install -d -m 0755 "$SITE_ROOT/releases"
cp -f Releases/* "$SITE_ROOT/releases/"
chmod -R a+rX "$SITE_ROOT"
python3 - "$SITE_ROOT/index.html" "$VERSION" <<'PY'
from pathlib import Path
import re, sys
p, version = Path(sys.argv[1]), sys.argv[2]
s = p.read_text(encoding='utf-8')
s = re.sub(r'Current release: <strong>[^<]+</strong>', f'Current release: <strong>{version}</strong>', s)
p.write_text(s, encoding='utf-8')
PY

python3 - "$FEED_URL" "$VERSION" <<'PY'
import json, sys, urllib.request
url, version = sys.argv[1].rstrip('/'), sys.argv[2]
assets = json.loads(urllib.request.urlopen(url + '/releases.win.json', timeout=30).read()).get('Assets', [])
assert any(a.get('Version') == version and a.get('Type') == 'Full' for a in assets), assets
print('feed ok:', version)
PY
for path in "releases.win.json" "E2ETester-$VERSION-full.nupkg" "E2ETester-win-Setup.exe"; do curl -fsS -o /dev/null "$FEED_URL/$path"; done
curl -fsS -o /dev/null "$LANDING_URL"
rm -rf "/tmp/e2etester-vpk-download-check"
vpk '[win]' download http --url "$FEED_URL" --channel "$CHANNEL" --outputDir /tmp/e2etester-vpk-download-check --timeout 2 --yes
rm -f "$notes"
echo "==> Released E2E Tester $VERSION"
