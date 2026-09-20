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

need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing required command: $1" >&2; exit 1; }; }
need dotnet; need git; need curl; need python3
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
python3 tools/check_publication.py
dotnet build "$SOLUTION" -c Release -p:EnableWindowsTargeting=true
dotnet run --project "$TEST_CSPROJ" -c Release --no-build -p:EnableWindowsTargeting=true

rm -rf Releases "publish/win-$VERSION"
mkdir -p Releases
if [[ -d "$SITE_ROOT/releases" ]]; then
  cp -f "$SITE_ROOT/releases"/E2ETester-*.nupkg Releases/ 2>/dev/null || true
  cp -f "$SITE_ROOT/releases"/RELEASES "$SITE_ROOT/releases"/assets.win.json "$SITE_ROOT/releases"/releases.win.json Releases/ 2>/dev/null || true
fi

dotnet publish "$APP_CSPROJ" -c Release -r win-x64 --no-self-contained \
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true \
  -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -p:Version="$VERSION" \
  -o "publish/win-$VERSION"

# Framework-dependent single-file apps retain a small .runtimeconfig.json sidecar;
# all managed/native app dependencies are bundled into E2ETester.Desktop.exe.
[[ -f "publish/win-$VERSION/E2ETester.Desktop.exe" ]] || { echo 'Missing desktop executable.' >&2; exit 1; }
[[ -f "publish/win-$VERSION/E2ETester.Desktop.runtimeconfig.json" ]] || { echo 'Missing runtime configuration.' >&2; exit 1; }
if find "publish/win-$VERSION" -maxdepth 1 -type f \( -name '*.dll' -o -name '*.deps.json' \) | grep -q .; then
  echo 'Unexpected loose DLL/deps file: publish must remain single-file.' >&2
  find "publish/win-$VERSION" -maxdepth 1 -type f -printf '%f\n' >&2
  exit 1
fi

notes="$(mktemp)"
printf '# E2E Tester %s\n\nStatic-feed release built from commit %s. Requires the .NET 10 Desktop Runtime.\n' "$VERSION" "$(git rev-parse --short HEAD)" > "$notes"
vpk '[win]' pack --packId "$PACK_ID" --packVersion "$VERSION" \
  --packDir "publish/win-$VERSION" --mainExe E2ETester.Desktop.exe \
  --packTitle 'E2E Tester' --packAuthors 'd3sync and E2E Tester contributors' \
  --releaseNotes "$notes" --runtime win-x64 --channel "$CHANNEL" --delta None \
  --icon "$ICON" --outputDir Releases --yes

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
