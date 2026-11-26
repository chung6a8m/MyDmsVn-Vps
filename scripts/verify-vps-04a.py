#!/usr/bin/env python3
"""VPS-04A source/compiled route gate. This is NOT live API, SQL or Linux-hosting acceptance."""
from __future__ import annotations
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
manifest = root / "docs" / "contracts"
old = json.loads((manifest / "vps-01-route-inventory.json").read_text(encoding="utf-8-sig"))
add = json.loads((manifest / "vps-02-route-additions.json").read_text(encoding="utf-8-sig"))
expected = {(e["httpMethod"], e["path"], e["controller"], e["action"])
            for e in old["endpoints"] + add["endpoints"]}
assert len(expected) == 57, "Frozen HTTP source inventory drift"

project = (root / "src/MyDmsVn.Server.AspNetCoreHost/MyDmsVn.Server.AspNetCoreHost.csproj").read_text()
for forbidden in ("Microsoft.AspNet.WebApi", "Microsoft.Owin", "System.Web"):
    assert forbidden not in project, f"Legacy package in net10 project: {forbidden}"
assert "<TargetFramework>net10.0</TargetFramework>" in project
server_sdk = json.loads((root / "src/MyDmsVn.Server.AspNetCoreHost/global.json").read_text())
assert server_sdk["sdk"]["version"] == "10.0.404"

for project_name in ("Contracts", "SharedKernel", "Server.Domain", "Server.Application", "Server.Infrastructure"):
    path = root / f"src/MyDmsVn.{project_name}/MyDmsVn.{project_name}.csproj"
    source = path.read_text(encoding="utf-8-sig")
    assert "Vps04Server" in source and "net10.0" in source, path
    assert "net48" in source, f"Legacy net48 test graph removed: {path}"
    lock = path.parent / "packages.vps04a.lock.json"
    assert lock.exists() and '"net10.0"' in lock.read_text(), f"Missing pinned net10 package graph: {path}"

for name in ("Server.AspNetCoreHost",):
    lock = root / f"src/MyDmsVn.{name}/packages.vps04a.lock.json"
    assert lock.exists(), f"Missing pinned ASP.NET Core package lock: {lock}"
for name in ("Server.Application", "Server.AspNetCoreHost"):
    lock = root / f"tests/MyDmsVn.{name}.Tests/packages.vps04a.lock.json"
    assert lock.exists(), f"Missing net10 test lock: {lock}"

if len(sys.argv) != 2:
    raise SystemExit("Usage: verify-vps-04a.py COMPILED_ROUTES.json")
actual_routes = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
actual = {(e["httpMethod"], e["path"], e["controller"], e["action"]) for e in actual_routes}
assert len(actual_routes) == 57 and len(actual) == 57, "Missing/duplicate compiled net10 routes/actions"
assert expected == actual, (
    f"HTTP controller/action parity mismatch; missing={sorted(expected - actual)} extra={sorted(actual - expected)}"
)
migration = json.loads((manifest / "vps-10-route-migration.json").read_text(encoding="utf-8-sig"))
by_route = {(r["httpMethod"], r["path"]): r for r in migration["routes"]}
assert len(by_route) == len(migration["routes"]) == 57
assert sum(r["activeOwner"] == "native" for r in migration["routes"]) == 4
for endpoint in actual_routes:
    row = by_route[endpoint["httpMethod"], endpoint["path"]]
    for key in ("order", "activeOwner", "actualController", "actualAction"):
        assert row[key] == endpoint[key], (row["id"], key, row[key], endpoint[key])
    assert row["baselineController"] == endpoint["controller"]
    assert row["baselineAction"] == endpoint["action"]
print("PASS: actual net10 endpoint map 57/57, native 4, bridge 53; not live SQL/HTTP or production acceptance")
