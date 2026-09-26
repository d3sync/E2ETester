"""Exercise the public CLI process, exit codes, import and JUnit contract."""
import json
import pathlib
import subprocess
import sys
import tempfile
import threading
import xml.etree.ElementTree as ET
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

class Api(BaseHTTPRequestHandler):
    calls = 0
    def log_message(self, *args): pass
    def do_GET(self):
        Api.calls += 1
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(b'{"id":1}')

exe = pathlib.Path(sys.argv[1]).resolve()
folder = pathlib.Path(tempfile.mkdtemp(prefix='E2ETester-cli-'))
server = ThreadingHTTPServer(('127.0.0.1', 0), Api)
threading.Thread(target=server.serve_forever, daemon=True).start()
url = f'http://127.0.0.1:{server.server_port}'
config = folder / 'host.json'
config.write_text(json.dumps({'allowHttp': True, 'allowedBaseUrls': [url]}))
suite = {'schemaVersion': '1.7', 'id': 'cli', 'title': 'CLI process', 'baseUrl': url,
         'cases': [{'id': 'read', 'title': 'Read', 'request': {'url': '/item'},
                    'assertions': [{'kind': 'jsonSchema', 'expected': json.dumps({'type': 'object', 'required': ['id']})}]}]}
package = folder / 'suite.jsee'
try:
    for name, expected in [('pass', 0), ('fail', 1), ('missing', 2)]:
        if name == 'fail': suite['cases'][0]['assertions'] = [{'expected': '404'}]
        if name == 'missing': suite['cases'][0]['request']['identityRef'] = 'missing'
        package.write_text(json.dumps(suite))
        output = folder / name
        result = subprocess.run([str(exe), 'run', '--package', str(package), '--config', str(config), '--output', str(output)], capture_output=True, text=True, timeout=40)
        assert result.returncode == expected, (name, result.stdout, result.stderr)
        xml = ET.parse(output / 'junit.xml')
        assert len(xml.findall('.//testcase')) == 1
        assert (output / 'report.json').exists() and (output / 'report.html').exists()
        report = json.loads((output / 'report.json').read_text())
        assert report['schemaVersion'] == '1.10', report.keys()
        print(f'PASS: CLI {name}, exit {expected}, JUnit and canonical evidence')
    assert Api.calls == 2, 'No retries or unauthorized identity requests'
    suite['cases'] = [{'id': f'fast-{i}', 'title': f'Fast response {i}', 'request': {'url': '/item'}, 'assertions': [{'expected': '200'}]} for i in range(30)]
    package.write_text(json.dumps(suite))
    result = subprocess.run([str(exe), 'run', '--package', str(package), '--config', str(config), '--output', str(folder / 'fast')], capture_output=True, text=True, timeout=40)
    assert result.returncode == 0, (result.stdout, result.stderr)
    assert Api.calls == 32, 'Every fast operation delivered exactly once'
    print('PASS: 30 fast completions never become spurious interrupted operations')
    spec = folder / 'openapi.yaml'
    spec.write_text("openapi: 3.0.3\npaths:\n  /item:\n    get:\n      responses:\n        '200': {description: OK}\n")
    result = subprocess.run([str(exe), 'import', '--input', str(spec), '--base-url', url, '--output', str(folder / 'import.jsee')], capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stderr
    assert Api.calls == 32 and len(json.loads((folder / 'import.jsee').read_text())['cases']) == 1
    print('PASS: CLI YAML import sends no requests')
    print(f'Evidence: {folder}')
finally:
    server.shutdown()
