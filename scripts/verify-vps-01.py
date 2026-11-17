#!/usr/bin/env python3
"""VPS-01 static guard: executable xUnit v3 suites and frozen HTTP route inventory."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
projects = sorted((root / "tests").glob("*/*.csproj"))
project_docs = {path: ET.parse(path).getroot() for path in projects}
helpers = [path for path, doc in project_docs.items()
           if any(node.text == "false" for node in doc.iter("IsTestProject"))]
suites = [path for path in projects if path not in helpers]
assert len(projects) == 17 and len(helpers) == 3 and len(suites) == 14, \
    "Test project inventory drift"
for path in suites:
    doc = project_docs[path]
    props = lambda key: [n.text for n in doc.iter(key) if n.text]
    refs = {n.attrib["Include"] for n in doc.iter("PackageReference")}
    target_frameworks = props("TargetFramework")
    if path.name == "MyDmsVn.Server.Application.Tests.csproj":
        assert target_frameworks == ["net48", "net10.0"], path
    elif path.name == "MyDmsVn.Server.AspNetCoreHost.Tests.csproj":
        assert target_frameworks == ["net10.0"], path
    else:
        assert target_frameworks == ["net48"], path
    assert props("OutputType") == ["Exe"], f"xUnit v3 executable missing: {path}"
    assert "xunit.v3" in refs and "xunit" not in refs, f"xUnit v3 package mismatch: {path}"
    assert "xunit.runner.visualstudio" in refs and "Microsoft.NET.Test.Sdk" in refs
    lock = path.parent / "packages.lock.json"
    if path.name != "MyDmsVn.Server.AspNetCoreHost.Tests.csproj":
        assert lock.exists() and '"xunit.v3"' in lock.read_text(encoding="utf-8-sig"), f"Missing locked xunit.v3: {path}"
    else:
        net10_lock=path.parent / "packages.vps04a.lock.json"
        assert net10_lock.exists() and '"xunit.v3"' in net10_lock.read_text(encoding="utf-8-sig"), f"Missing locked net10 xunit.v3: {path}"

packages = (root / "Directory.Packages.props").read_text(encoding="utf-8-sig")
assert 'PackageVersion Include="xunit.v3" Version="3.2.2"' in packages
assert 'PackageVersion Include="xunit"' not in packages

contract_root = root / "docs/contracts"
inventory = json.loads((contract_root / "vps-01-route-inventory.json").read_text(encoding="utf-8-sig"))
entries = inventory["endpoints"]
assert inventory["schemaVersion"] == 1 and len(entries) == 29
assert len({(e["httpMethod"], e["path"], e["controller"], e["action"]) for e in entries}) == 29
assert len({e["controller"] for e in entries}) == 9
corpora = {"web2-v1", "web3-v1", "web4-v1", "web5-resolution-v1", "web6-resend-eligibility-v1"}
for e in entries:
    assert e["verification"] == "route-source-only"
    assert e["httpMethod"] in {"GET", "POST", "PUT", "DELETE"}
    assert e["path"].startswith("/api/v")
    assert e["authentication"] in {"anonymous", "bearer", "cookie-endpoint"}
    assert e["fixtureCorpus"] is None or e["fixtureCorpus"] in corpora
    if e["authentication"] == "anonymous":
        assert e["path"] == "/api/v1/health"
    if e["authentication"] == "cookie-endpoint":
        assert e["path"].startswith("/api/v1/auth/")
assert len((contract_root / "corpus-blobs.sha1").read_text(encoding="utf-8-sig").splitlines()) == 178
# The four executed Windows suites must carry numeric floors in the *smoke* matrix,
# not only in the production-build matrix. Guard against accidental movement.
workflow = (root / ".github/workflows/vps-foundation.yml").read_text(encoding="utf-8-sig")
sections = workflow.split("\n  windows-smoke:\n", 1)
assert len(sections) == 2, "Missing windows-smoke job"
smoke_matrix = sections[1].split("\n    steps:\n", 1)[0]
expected_smoke_floors = {
    "application": 363,
    "architecture": 13,
    "desktop-session": 212,
    "legacy-http-host": 640,
}
for test_id, floor in expected_smoke_floors.items():
    row = f"          - id: {test_id}\n            minimum_tests: {floor}\n"
    assert smoke_matrix.count(row) == 1, f"Missing smoke floor {test_id}={floor}"
assert smoke_matrix.count("minimum_tests:") == len(expected_smoke_floors), "Unexpected smoke floor inventory"

print("VPS-01 static topology: PASS 13 legacy + 1 net10 executable xUnit v3 projects; 29 routes; 178 fixtures; 4 smoke floors")


# VPS-02 retains the 29 legacy routes; additions and Desktop mappings are versioned independently.
additive = json.loads((contract_root / "vps-02-route-additions.json").read_text(encoding="utf-8-sig"))
new_entries = additive["endpoints"]
assert additive["schemaVersion"] == 1 and len(new_entries) == 28
new_signatures = {(e["httpMethod"], e["path"]) for e in new_entries}
old_signatures = {(e["httpMethod"], e["path"]) for e in entries}
assert len(new_signatures) == 28 and old_signatures.isdisjoint(new_signatures)
assert len({e["controller"] for e in new_entries}) == 6
assert all(e["authentication"] == "bearer" and e["desktopApiMode"] == "enabled" for e in new_entries)
matrix = json.loads((contract_root / "vps-02-desktop-method-matrix.json").read_text(encoding="utf-8-sig"))
methods = matrix["methods"]
assert matrix["schemaVersion"] == 1 and len(methods) == 46
assert len({(m["interface"], m["method"]) for m in methods}) == 46
assert len([m for m in methods if m["status"] == "NEW"]) == 28
assert all((m["httpMethod"], m["path"]) in old_signatures | new_signatures for m in methods)
assert all(m["status"] in {"NEW", "BASELINE"} for m in methods)
print("VPS-02 static source map: PASS 28 new routes, 46 Desktop methods; not HTTP/SQL acceptance")
