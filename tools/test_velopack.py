"""Apply an upgrade and rollback inside an isolated extracted Velopack portable bundle.
Usage: python tools/test_velopack.py <portable.zip> <feed-folder> <new-version> <old-version>
The feed must contain both full packages. No installer or user workspace is touched.
"""
import pathlib
import subprocess
import sys
import tempfile
import time
import zipfile

portable, feed, newer, older = sys.argv[1:]
root = pathlib.Path(tempfile.mkdtemp(prefix="E2ETester-update-test-"))
with zipfile.ZipFile(portable) as archive:
    for entry in archive.infolist():
        if not (root / entry.filename).resolve().is_relative_to(root.resolve()):
            raise RuntimeError("Unsafe archive entry")
    archive.extractall(root)
exe = root / "current/E2ETester.Desktop.exe"
for name, version in (("upgrade", newer), ("rollback", older)):
    evidence = root / (name + ".png")
    subprocess.run([str(exe), f"--smoke={evidence}", f"--update-exercise={pathlib.Path(feed).resolve()}", f"--update-target={version}"], timeout=60, check=True)
    success = pathlib.Path(str(evidence) + ".applied.png.version.txt")
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline and not success.exists():
        error = pathlib.Path(str(evidence) + ".error.txt")
        restart_error = pathlib.Path(str(evidence) + ".applied.png.error.txt")
        if error.exists() or restart_error.exists():
            raise RuntimeError((error if error.exists() else restart_error).read_text())
        time.sleep(0.2)
    if not success.exists() or success.read_text() != version:
        raise RuntimeError("Version restart verification failed: " + str(root))
    # Let the rendering smoke shut down its window before the next version operation.
    time.sleep(1)
    print("PASS:", pathlib.Path(str(evidence) + ".prepared.txt").read_text())
print("PASS: real Velopack upgrade and rollback; evidence:", root)
