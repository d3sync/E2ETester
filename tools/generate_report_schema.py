"""Regenerate the report schema from the package schema and report evidence contract."""
import copy
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
package = json.loads((root / "schemas/test-package.v1.2.schema.json").read_text(encoding="utf-8-sig"))
defs = package.pop("$defs")
package.pop("$id")
package.pop("$schema")
package["properties"]["schemaVersion"] = {"enum": ["1.0", "1.1", "1.2"]}
defs["package"] = package
string = {"type": "string"}
boolean = {"type": "boolean"}
integer = {"type": "integer", "minimum": 0}
date = {"type": "string", "format": "date-time"}
verdict = {"enum": ["pass", "fail", "inconclusive"]}
def ref(name): return {"$ref": "#/$defs/" + name}
def nullable(value): return {"anyOf": [copy.deepcopy(value), {"type": "null"}]}
def array(value): return {"type": "array", "items": copy.deepcopy(value)}
def obj(**props): return {"type": "object", "additionalProperties": False, "required": list(props), "properties": copy.deepcopy(props)}
def mapping(value): return {"type": "object", "additionalProperties": copy.deepcopy(value)}

defs["checkResult"] = obj(id=string, required=boolean, verdict=verdict, expected=string, actual=string, message=string)
defs["verifiedIdentity"] = obj(issuer=string, subject=string, displayName=string, expiresAt=date, source={"const": "validated_oidc_id_token"})
defs["response"] = obj(statusCode=nullable({"type": "integer", "minimum": 100, "maximum": 599}), headers=mapping(array(string)), body=string, truncated=boolean, capturedBytes=integer, durationMs=integer, errorCode=nullable(string), error=nullable(string))
defs["queryResult"] = obj(startedAt=date, durationMs=integer, columns=array(obj(name=string, dataType=string)), rows=array(mapping({})), truncated=boolean, errorCode=nullable(string), error=nullable(string))
defs["queryEvidence"] = obj(id=string, query=ref("query"), result=ref("queryResult"), checkId=nullable(string), assessment=nullable(ref("checkResult")), purpose={"enum": ["exploration", "verification"]})
defs["fixture"] = obj(evidence=ref("queryEvidence"), rowIndex=integer)
defs["manualAssessment"] = obj(id=string, createdAt=date, reviewer=string, verdict=verdict, reason=string, automatedVerdict=verdict, evidenceHash={"type": "string", "pattern": "^[A-F0-9]{64}$"}, reviewerSource={"const": "self_declared"})
defs["attempt"] = obj(id=string, caseId=string, caseTitle=string, startedAt=date, finishedAt=nullable(date), state={"enum": ["running", "completed", "cancelled", "interrupted"]}, verdict=verdict, identityAlias=string, authKind={"enum": ["none", "bearer", "apiToken"]}, verifiedIdentity=nullable(ref("verifiedIdentity")), effectiveUrl=string, request=ref("request"), changes=array(obj(path=string, original=string, current=string)), assertions=array(ref("assertion")), response=nullable(ref("response")), checks=array(ref("checkResult")), fixture=nullable(ref("fixture")), databasePlans=array(obj(check=ref("databaseCheck"), query=ref("query"))), databaseEvidence=array(ref("queryEvidence")))
defs["run"] = obj(id=string, startedAt=date, finishedAt=nullable(date), packageHash=string, package=ref("package"), baseUrl=string, attempts=array(ref("attempt")))
defs["attempt"]["properties"]["manualAssessments"] = array(ref("manualAssessment"))
defs["attempt"]["properties"]["executionId"] = nullable(string)
defs["attempt"]["properties"]["captureSources"] = {"type": "object", "additionalProperties": string}
defs["response"]["properties"]["bodyEncoding"] = {"enum": ["text", "base64"]}
defs["response"]["properties"].update(httpVersion=string, redirects=array(obj(statusCode=integer, url=string, location=string)))
defs["response"]["properties"]["requests"] = array(obj(method=string, url=string, httpVersion=string, headers=mapping(array(string)), body=string, bodyEncoding={"enum": ["text", "base64"]}, hasBody=boolean, bodyBytes=integer, note=string))
defs["attempt"]["required"].append("manualAssessments")
defs["summary"] = obj(verdict=verdict, totalCases=integer, executedCases=integer, attempts=integer, hadFailures=boolean, effectiveAttemptPolicy={"const": "latest attempt per case; all historical attempts retained"})
defs["coverage"] = obj(caseId=string, title=string, parentId=nullable(string), attemptId=nullable(string), verdict=nullable(verdict), state={"enum": ["NotRun", "Running", "Completed", "Cancelled", "Interrupted"]})
defs["coverage"]["properties"].update(manualVerdict=nullable(verdict), manualAssessmentId=nullable(string), manualAssessmentIsCurrent=nullable(boolean))
defs["coverage"]["required"].extend(["manualVerdict", "manualAssessmentId", "manualAssessmentIsCurrent"])
defs["reportCheck"] = obj(attemptId=string, checkId=string, required=boolean, verdict=verdict, expected={}, actual={}, expectedText=string, actualText=string, message=string, evidencePointer={"type": "string", "pattern": r"^/run/attempts/[0-9]+/checks/[0-9]+$"})
schema = obj(schemaVersion={"const": "1.5"}, reportId=string, exportedAt=date, provisional=boolean, applicationVersion=string, targetBuild=nullable(string), summary=ref("summary"), coverage=array(ref("coverage")), checkEvidence=array(ref("reportCheck")), redactions=string, run=ref("run"))
schema = {"$schema": "https://json-schema.org/draft/2020-12/schema", "$id": "https://e2etester.local/schemas/test-report.v1.5.schema.json", "title": "E2ETester report 1.5", **schema, "$defs": defs}
(root / "schemas/test-report.v1.5.schema.json").write_text(json.dumps(schema, indent=2) + "\n", encoding="utf-8")
