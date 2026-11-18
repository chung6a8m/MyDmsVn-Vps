#!/usr/bin/env python3
"""Enumerate source MediatR messages/handlers/dispatch without scanning vendored code.

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
           and "MyDmsVn.Vps06A.MediatorCanary" not in p.parts]
results = {key: [] for key in patterns}
digester = hashlib.sha256()
for path in sources:
    relative = path.relative_to(root).as_posix()
    data = path.read_text(encoding="utf-8-sig", errors="replace")
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
    "purpose": "VPS-06A lexical inventory; not a generated production handler count",
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
