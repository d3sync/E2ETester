"""Exercise the real MCP stdio protocol with no third-party Python dependencies."""
import json
import pathlib
import queue
import subprocess
import tempfile
import threading
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

root = pathlib.Path(__file__).resolve().parents[1]
class Api(BaseHTTPRequestHandler):
    calls = 0
    def log_message(self, *args):
        pass
    def do_GET(self):
        Api.calls += 1
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(b'{"ok":true}')

api = ThreadingHTTPServer(("127.0.0.1", 0), Api)
threading.Thread(target=api.serve_forever, daemon=True).start()
base_url = f"http://127.0.0.1:{api.server_port}"
with tempfile.TemporaryDirectory(prefix="e2etester-mcp-") as directory:
    config = pathlib.Path(directory) / "config.json"
    config.write_text(json.dumps({"workspace": str(pathlib.Path(directory) / "workspace"), "allowedBaseUrls": [base_url], "allowHttp": True, "allowAuthoring": True}), encoding="utf-8")
    command = [sys.argv[1]] if len(sys.argv) > 1 else ["dotnet", str(root / "src/E2ETester.Mcp/bin/Release/net10.0/E2ETester.Mcp.dll")]
    process = subprocess.Popen([*command, "--config", str(config)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    received = queue.Queue()
    def reader():
        for line in process.stdout:
            received.put(json.loads(line))
    threading.Thread(target=reader, daemon=True).start()
    def send(value):
        process.stdin.write(json.dumps({"jsonrpc": "2.0", **value}) + "\n")
        process.stdin.flush()
    def response(identifier):
        while True:
            value = received.get(timeout=15)
            if value.get("id") == identifier:
                assert "error" not in value, value
                return value["result"]
    try:
        send({"id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "integration-test", "version": "1"}}})
        assert response(1)["serverInfo"]["name"] == "E2E Tester"
        send({"method": "notifications/initialized"})
        send({"id": 2, "method": "tools/list", "params": {}})
        names = {tool["name"] for tool in response(2)["tools"]}
        assert {"prepare_case", "execute_prepared", "get_operation", "get_report", "run_saved_query", "prepare_environment", "restore_environment", "list_preparations"} <= names
        send({"id": 3, "method": "tools/call", "params": {"name": "capabilities", "arguments": {}}})
        result = json.loads(response(3)["content"][0]["text"])
        assert result["ok"] and result["result"]["allowHttp"] and not result["result"]["allowSavedQueries"]
        send({"id": 4, "method": "tools/call", "params": {"name": "validate_package", "arguments": {"packageJson": "{}"}}})
        result = json.loads(response(4)["content"][0]["text"])
        assert not result["ok"] and result["error"] == "operation_rejected"
        identifier = 4
        def call(name, arguments):
            global identifier
            identifier += 1
            send({"id": identifier, "method": "tools/call", "params": {"name": name, "arguments": arguments}})
            value = json.loads(response(identifier)["content"][0]["text"])
            assert value["ok"], value
            return value["result"]
        package = {"schemaVersion": "1.2", "id": "protocol-test", "title": "Protocol test", "cases": [{"id": "health", "title": "Health", "request": {"url": "/health"}, "assertions": [{"expected": "200"}]}]}
        call("save_package", {"packageJson": json.dumps(package)})
        library = call("list_packages", {})
        assert call("get_package", {"packageId": library[0]["id"]})["id"] == "protocol-test"
        run = call("start_run", {"packageJson": json.dumps(package), "baseUrl": base_url})
        def execute(case_id):
            prepared = call("prepare_case", {"runId": run["id"], "caseId": case_id})
            arguments = {"executionId": prepared["executionId"]}
            call("execute_prepared", arguments)
            call("execute_prepared", arguments)
            deadline = time.monotonic() + 10
            while call("get_operation", arguments)["state"] == "running":
                assert time.monotonic() < deadline, "Operation timed out"
                time.sleep(0.02)
        execute("health")
        variation = call("add_variation", {"runId": run["id"], "caseId": "health", "requestJson": json.dumps({"url": "/variation"}), "title": "Extra test"})
        execute(variation["id"])
        report = call("get_report", {"runId": run["id"]})
        assert Api.calls == 2 and report["summary"]["verdict"] == "pass" and report["summary"]["attempts"] == 2
        call("finish_run", {"runId": run["id"]})
        assert not call("get_report", {"runId": run["id"]})["provisional"]
        print(f"PASS: MCP initialize, discovery ({len(names)} tools), call, safe error, permissions")
        print("PASS: MCP package library, API execution, duplicate delivery, variation, finish and canonical report")
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
api.shutdown()
api.server_close()
