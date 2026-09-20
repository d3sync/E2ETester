"""Build the additive 1.2 package contract while retaining historical schemas."""
import json
from pathlib import Path
root = Path(__file__).resolve().parents[1]
schema = json.loads((root / "schemas/test-package.v1.1.schema.json").read_text(encoding="utf-8-sig"))
schema["$id"] = "https://e2etester.local/schemas/test-package.v1.2.schema.json"
schema["title"] = "E2ETester package 1.2"
schema["properties"]["schemaVersion"] = {"const": "1.2"}
defs = schema["$defs"]
defs["assertion"]["properties"]["kind"] = {"enum": ["status", "bodyContains", "jsonEquals", "headerEquals", "statusIn", "headerExists", "jsonExists", "jsonGreaterThan", "jsonLessThan", "jsonArrayCount", "durationLessThan"]}
defs["bodyPart"] = {"type": "object", "additionalProperties": False, "properties": {
    "name": {"type": "string", "minLength": 1}, "value": {"type": "string"}, "fileName": {"type": "string"}, "contentType": {"type": "string"},
    "binary": {"type": "boolean"}, "enabled": {"type": "boolean"}, "required": {"type": "boolean"}, "sensitive": {"type": "boolean"}}}
defs["request"]["properties"].update(bodyKind={"enum": ["text", "formUrlEncoded", "multipart", "binary"]}, parts={"type": "array", "maxItems": 1000, "items": {"$ref": "#/$defs/bodyPart"}}, useCookies={"type": "boolean"})
defs["request"]["properties"].update(httpVersion={"enum": ["1.1", "2.0", "3.0"]}, proxyUrl={"type": "string"}, clientCertificateThumbprint={"type": "string"}, followRedirects={"type": "boolean"}, maxRedirects={"type": "integer", "minimum": 0, "maximum": 10})
defs["databaseCheck"]["properties"]["kind"] = {"enum": ["rowCount", "scalarEquals", "scalarNotEquals", "scalarGreaterThan", "scalarLessThan", "rowExists", "rowAbsent"]}
defs["databaseCheck"]["properties"].update(pollTimeoutSeconds={"type": "integer", "minimum": 0, "maximum": 600}, pollIntervalMilliseconds={"type": "integer", "minimum": 100, "maximum": 60000})
(root / "schemas/test-package.v1.2.schema.json").write_text(json.dumps(schema, indent=2) + "\n", encoding="utf-8")
