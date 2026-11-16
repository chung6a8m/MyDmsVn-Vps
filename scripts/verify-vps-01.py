#!/usr/bin/env python3
"""VPS-01 static guard: executable xUnit v3 suites and frozen HTTP route inventory."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
projects = sorted((root / "tests").glob("*/*.csproj"))
suites = [p for p in projects if p.parent.name != "MyDmsVn.WebOperation.CrashHost"]
assert len(projects) == 13 and len(suites) == 12, "Test project inventory drift"
for path in suites:
    doc = ET.parse(path).getroot()
    props = lambda key: [n.text for n in doc.iter(key) if n.text]
    refs = {n.attrib["Include"] for n in doc.iter("PackageReference")}
    assert props("TargetFramework") == ["net48"], path
    assert props("OutputType") == ["Exe"], f"xUnit v3 executable missing: {path}"
    assert "xunit.v3" in refs and "xunit" not in refs, f"xUnit v3 package mismatch: {path}"
    assert "xunit.runner.visualstudio" in refs and "Microsoft.NET.Test.Sdk" in refs
    lock = path.parent / "packages.lock.json"
    assert lock.exists() and '"xunit.v3"' in lock.read_text(encoding="utf-8-sig"), f"Missing locked xunit.v3: {path}"

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
print("VPS-01 static topology: PASS 12 executable xUnit v3 projects; 29 routes; 178 fixtures")
