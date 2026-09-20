"""Test persistent WebView2 sign-in data across fully separate app/browser processes."""
import pathlib
import subprocess
import sys
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Page(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        if self.path == "/seed":
            self.send_header("Set-Cookie", "restart-proof=saved; Max-Age=3600; Path=/; HttpOnly; SameSite=Lax")
        self.end_headers()
        received = "restart-proof=saved" in self.headers.get("Cookie", "")
        self.wfile.write(("<html><body>Browser restart test<script>window.testPage=true;window.cookieReceived=" + str(received).lower() + ";</script></body></html>").encode())


root = pathlib.Path(tempfile.mkdtemp(prefix="E2ETester-browser-restart-"))
server = ThreadingHTTPServer(("127.0.0.1", 0), Page)
threading.Thread(target=server.serve_forever, daemon=True).start()
try:
    for mode in ("seed", "verify", "cleared"):
        shot = root / (mode + ".png")
        executable = sys.argv[2] if mode != "seed" and len(sys.argv) > 2 else sys.argv[1]
        result = subprocess.run([str(pathlib.Path(executable).resolve()), f"--smoke={shot}",
                                 f"--browser-persistence={mode}", f"--browser-test-data={root / 'profile'}",
                                 f"--browser-test-url=http://127.0.0.1:{server.server_port}/{mode}"], timeout=65)
        if result.returncode:
            error = pathlib.Path(str(shot) + ".error.txt")
            raise RuntimeError(error.read_text() if error.exists() else f"App exited {result.returncode}")
        bindings = pathlib.Path(str(shot) + ".bindings.log").read_text()
        if bindings.strip():
            raise RuntimeError(bindings)
        print("PASS: separate app/browser process " + mode)
    print("PASS: persistent HTTP-only cookie, local storage, password prompt enabled, explicit clearing. Evidence:", root)
finally:
    server.shutdown()
