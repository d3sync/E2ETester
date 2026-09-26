"""Validate exported reports offline: pip install jsonschema==4.26.0, then pass JSON paths."""
import json
import sys
from pathlib import Path
from jsonschema import Draft202012Validator, FormatChecker

if len(sys.argv) < 2:
    raise SystemExit("Pass at least one exported report JSON path.")
for name in sys.argv[1:]:
    document = json.loads(Path(name).read_text(encoding="utf-8-sig"))
    version = document.get("schemaVersion")
    if version not in ("1.2", "1.3", "1.4", "1.5", "1.6", "1.7", "1.8", "1.9", "1.10"):
        raise ValueError("Unsupported report version")
    schema = json.loads((Path(__file__).resolve().parents[1] / f"schemas/test-report.v{version}.schema.json").read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    validator.validate(document)
    for check in document["checkEvidence"]:
        node = document
        for part in check["evidencePointer"].split("/")[1:]:
            node = node[int(part)] if isinstance(node, list) else node[part]
        if node["id"] != check["checkId"]:
            raise ValueError("Check evidence pointer resolves to a different check.")
    print("PASS:", name)
