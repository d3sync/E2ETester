"""Audit Git-visible files without printing secret values; optionally stage public release assets."""
import argparse
import hashlib
import json
import pathlib
import re
import shutil
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
PRIVATE = re.compile(r"mar" + r"point|mp" + r"nt\.cloud|IS4" + r"Service|CustomRedirect" + r"Validator|api-" + r"common|Platform" + r"Test", re.I)
SECRETS = [
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|AKIA[A-Z0-9]{16})\b"),
    re.compile(r"\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\b"),
]
ASSETS = {'README.md', 'LICENSE', 'CHANGELOG.md', 'CONTRIBUTING.md', 'CODE_OF_CONDUCT.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md', 'docs', 'schemas', 'samples', 'assets', 'skills'}
MEDIA = {'.png','.jpg','.jpeg','.webp','.gif','.ico','.svg','.pdf','.mp4','.webm'}

def public_files():
    with tempfile.TemporaryDirectory(prefix='e2e-publication-') as temporary:
        if (ROOT / '.git').exists():
            command = ['git', '-C', str(ROOT)]
        else:
            subprocess.run(['git', 'init', '--bare', '--quiet', temporary], check=True)
            command = ['git', '--git-dir=' + temporary, '--work-tree=' + str(ROOT)]
        # Include tracked files even when newly ignored: .gitignore cannot untrack secrets.
        result = subprocess.run(command + ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT, check=True, capture_output=True)
        return sorted(set(p for p in result.stdout.decode('utf-8').split('\0') if p))

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--stage', type=pathlib.Path); args = parser.parse_args()
    files = public_files(); findings = []
    review_file = ROOT / 'docs/public-media-review.json'
    reviews = json.loads(review_file.read_text(encoding='utf-8'))['files'] if review_file.exists() else {}
    if (ROOT / '.git').exists():
        ignored = subprocess.run(['git','-C',str(ROOT),'check-ignore','--no-index','-z','--stdin'], input=('\0'.join(files)+'\0').encode(), capture_output=True)
        if ignored.returncode not in (0,1): raise RuntimeError('Could not verify tracked ignore rules.')
        for name in ignored.stdout.decode('utf-8').split('\0'):
            if name: findings.append((name, 'tracked file matches ignore rules; remove it from the index'))
    for name in files:
        path = ROOT / name
        if path.suffix.lower() in MEDIA:
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            if reviews.get(name, {}).get('sha256') != digest:
                findings.append((name, 'new or changed media requires visual privacy review; update the review record only after inspection'))
        if path.suffix.lower() in {'.exe','.dll','.pdb','.db','.sqlite','.protected','.pfx','.p12','.key','.pem','.har','.zip'} or name.startswith(('private/','artifacts/','.vs/','workspaces/')) or '.local.' in name or '.private.' in name:
            findings.append((name, 'private/generated file is Git-visible'))
        if PRIVATE.search(name): findings.append((name, 'private integration filename'))
        try: content = path.read_text(encoding='utf-8-sig')
        except UnicodeError:
            if path.suffix.lower() not in MEDIA: findings.append((name, 'unreviewed binary content'))
            continue
        if name != '.gitignore' and PRIVATE.search(content): findings.append((name, 'private integration content'))
        if any(pattern.search(content) for pattern in SECRETS): findings.append((name, 'credential-shaped content'))
    if findings:
        for name, reason in findings: print(f'FAIL: {name}: {reason}')
        raise SystemExit(1)
    print(f'PASS: {len(files)} Git-visible files checked; no configured private-content or credential-pattern matches.')
    if args.stage:
        for name in files:
            relative = pathlib.PurePosixPath(name)
            if relative.parts[0] not in ASSETS: continue
            target = args.stage / name; target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / name, target)
        print('Staged reviewed Git-visible release assets only.')

if __name__ == '__main__': main()
