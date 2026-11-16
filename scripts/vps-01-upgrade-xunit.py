#!/usr/bin/env python3
"""One-time xUnit v3 source migration. Used by the branch-only lock refresh job."""
from __future__ import annotations
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[1]
changes = []
for file in sorted((ROOT / "tests").rglob("*.cs")):
    original_bytes = file.read_bytes()
    bom = original_bytes.startswith(b"\xef\xbb\xbf")
    content = original_bytes[3:] if bom else original_bytes
    original = content.decode("utf-8")
    eol = "\r\n" if "\r\n" in original else "\n"
    content_lf = original.replace("\r\n", "\n")
    updated = content_lf.replace("using Xunit.Abstractions;\n", "").replace(
        "Xunit.Abstractions.ITestOutputHelper", "Xunit.ITestOutputHelper")
    if "IAsyncLifetime" in updated:
        updated = re.sub(
            r"\bpublic\s+async\s+Task\s+(InitializeAsync|DisposeAsync)\s*\(",
            lambda m: "public async ValueTask " + m.group(1) + "(",
            updated)
        updated = re.sub(
            r"\bpublic\s+Task\s+InitializeAsync\s*\(\s*\)\s*=>\s*Task.CompletedTask\s*;",
            "public ValueTask InitializeAsync() => default;", updated)
        if re.search(r"\bpublic\s+Task\s+(InitializeAsync|DisposeAsync)\s*\(", updated):
            raise SystemExit(f"Unhandled IAsyncLifetime method in {file.relative_to(ROOT)}")
    if "ITestOutputHelper" in updated and "using Xunit;" not in updated:
        updated = "using Xunit;\n" + updated
    if updated != content_lf:
        file.write_bytes((b"\xef\xbb\xbf" if bom else b"") +
                         updated.replace("\n", eol).encode("utf-8"))
        changes.append(str(file.relative_to(ROOT)))
print(f"VPS-01 transformed {len(changes)} test C# files:")
for name in changes:
    print(name)
