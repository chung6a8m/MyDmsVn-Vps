#!/usr/bin/env python3
"""Enumerate frozen *legacy* MediatR source without scanning vendored code.

This is lexical inventory (for VPS-06A handoff), NOT generator acceptance of each
production handler. Compile-time generator registration for production is VPS-06B.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
patterns = {
    "handler_symbols": re.compile(r"\bIRequestHandler\s*<"),
    "request_derivations": re.compile(r"\b(?:ApplicationRequest|AuthorizedActorApplicationRequest)\s*<"),
    "pipeline_behaviors": re.compile(r"\bIPipelineBehavior\s*<"),
    "sender_interfaces": re.compile(r"\b(?:ISender|IMediator)\b"),
    "sender_calls": re.compile(r"\.Send\s*\("),
    "registration_calls": re.compile(r"\b(?:AddMediatR|AddServerApplication)\s*\("),
}
sources = sorted([
    *root.joinpath("src").rglob("*.cs"),
    *root.joinpath("tests").rglob("*.cs")
])
sources = [p for p in sources if not any(name in {"bin", "obj"} for name in p.parts)
           and "MyDmsVn.Vps06A.MediatorCanary" not in p.parts
           and not p.name.startswith("Vps06b")
           and p.name not in {"Vps06bInternals.cs", "GeneratedMediatorRegistration.cs"}
           # VPS-10.1 composition tests target only net10.0 / generated Mediator,
           # not the frozen legacy MediatR path. Keep all legacy counts unchanged.
           and p.relative_to(root).as_posix() not in {
               "tests/MyDmsVn.Server.AspNetCoreHost.Tests/Vps10HostCompositionTests.cs",
               # VPS-10.2: this controller is net10-only and uses generated
               # Mediator, not the frozen legacy MediatR implementation.
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Catalog/CatalogLookupController.cs",
               # VPS-10.6A native-only generated Mediator, no legacy MediatR.
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Catalog/ProductsController.cs",
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Catalog/WarehousesController.cs",
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Catalog/EmployeesController.cs",
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Catalog/CustomersController.cs",
               # VPS-10.6B native-only generated Mediator.
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Security/SecurityController.cs",
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Foundation/FoundationController.cs",
               # VPS-10.4A: exactly this native-only controller contributes one
               # generated IMediator injection and two Send calls, not MediatR.
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Sales/SalesReceiptsController.cs",
               # VPS-10.5A: only net10 generated Mediator read controllers.
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Receipts/GoodsReceiptsController.cs",
               "src/MyDmsVn.Server.AspNetCoreHost/Features/Inventory/InventoryController.cs",
               # VPS-10.4B: only the new native-only HTTPS/SQL restart test
               # partial. Three Gateway.Send calls are HTTP, not legacy MediatR.
               "tests/MyDmsVn.Server.AspNetCoreHost.Tests/Vps04bWeb6LiveSqlTests.SalesRestart.cs",
               # VPS-10.5B: native-only real HTTPS Gateway.Send calls, not MediatR.
               "tests/MyDmsVn.Server.AspNetCoreHost.Tests/Vps04bWeb6LiveSqlTests.ReceiptRestart.cs",
           }]
# Keep the 06A negative/frozen lexical inventory of the net48/legacy code
# path. Conditional VPS-06B native source does not alter that original baseline.
def legacy_slice(data: str) -> str:
    lines, active, out = data.splitlines(keepends=True), [], []
    for line in lines:
        m = re.match(r"^\s*#if\s+(!?)VPS06B_MEDIATOR\s*$", line)
        if m:
            active.append(m.group(1) == "!")
            continue
        if active and re.match(r"^\s*#else\s*$", line):
            active[-1] = not active[-1]
            continue
        if active and re.match(r"^\s*#endif\s*$", line):
            active.pop()
            continue
        if all(active):
            out.append(line)
    assert not active, "Unbalanced VPS06B_MEDIATOR source conditional"
    return "".join(out)
results = {key: [] for key in patterns}
digester = hashlib.sha256()
for path in sources:
    relative = path.relative_to(root).as_posix()
    data = legacy_slice(path.read_text(encoding="utf-8-sig", errors="replace"))
    digester.update(relative.encode("utf-8") + b"\0" + data.encode("utf-8") + b"\0")
    for key, pattern in patterns.items():
        for m in pattern.finditer(data):
            line = data.count("\n", 0, m.start()) + 1
            snippet = " ".join(data[m.start():m.start() + 110].split())
            results[key].append({"path": relative, "line": line, "expression": snippet})

counts = {k: len(entries) for k, entries in results.items()}
# Frozen at VPS-06A baseline main ba1c0049; intentional production handler
# changes in VPS-06B require a reviewed inventory rebaseline, not silent drift.
expected = {
    "handler_symbols": 139,
    "request_derivations": 62,
    "pipeline_behaviors": 3,
    "sender_interfaces": 171,
    "sender_calls": 495,
    "registration_calls": 99,
}
assert counts == expected, f"MediatR source inventory drift: expected {expected}; actual {counts}"
report = {
    "schemaVersion": 1,
    "purpose": "VPS-06A frozen legacy lexical inventory (VPS-06B conditional source excluded); not a production generated handler count",
    "sourceDigestSha256": digester.hexdigest(),
    "scannedSourceFiles": len(sources),
    "counts": counts,
    "occurrences": results,
}
parser = argparse.ArgumentParser()
parser.add_argument("--output", default="vps06a-mediator-inventory.json")
args = parser.parse_args()
Path(args.output).write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print("PASS VPS06A_MEDIATR_INVENTORY " + json.dumps({
    "files": len(sources), "sha256": digester.hexdigest(), "counts": counts
}, sort_keys=True))
