#!/usr/bin/env python3
"""VPS-04B/VPS-10.7: native source, evaluated Compile items and runtime graph."""
import argparse
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "src" / "MyDmsVn.Server.AspNetCoreHost"
PROJECT = HOST / "MyDmsVn.Server.AspNetCoreHost.csproj"
ARTIFACT = HOST / "bin" / "Release" / "net10.0" / "MyDmsVn.Server.AspNetCoreHost.deps.json"

BLOCKED_EXACT = {
    "system.web", "system.security.accesscontrol",
    "system.security.principal.windows", "system.io.filesystem.accesscontrol",
    "system.io.pipes.accesscontrol", "system.threading.accesscontrol",
    "microsoft.aspnetcore.owin", "owin", "newtonsoft.json",
}
BLOCKED_PREFIXES = ("microsoft.owin", "microsoft.aspnet.webapi", "system.web.")
RETIRED_FILES = {"compatwebapi.cs", "wiredom.cs", "net10routetable.cs", "net10wirejson.cs"}
RETIRED_CODE = re.compile(
    r"\b(?:System\.Web\.Http|Microsoft\.Owin|WireDom|WireToken(?:Type)?|WireObject|WireArray|"
    r"WireValue|WireProperty|Net10RouteTable|Net10WireJson|CoreRequestContextExtension|"
    r"GetOwinContext|ApiHttpResponseMessage|VPS05_STJ_(?:INGRESS|EGRESS))\b"
    r"|\b(?:MethodInfo|Action|ActionMethod)\s*\.\s*Invoke\s*\("
    r"|\bGetCustomAttribute\s*<\s*(?:Route|RoutePrefix|Http(?:Get|Post|Put|Delete|Patch))Attribute"
)


def validate_source(host):
    project = host / PROJECT.name
    root = ET.parse(project).getroot()
    # The SDK default Compile glob is sufficient. No explicit source inclusion,
    # path indirection or Link metadata may silently add a second HTTP owner.
    for node in root.findall(".//Compile"):
        assert "Include" not in node.attrib, "Explicit Compile source input in native Host"
        assert "Link" not in node.attrib and "LinkBase" not in node.attrib, "Linked Host source"
        assert node.find("Link") is None and node.find("LinkBase") is None, "Linked Host source"
    assert not re.search(r"VPS05_STJ_(?:INGRESS|EGRESS)", project.read_text(encoding="utf-8-sig"))
    files = [p for p in host.rglob("*.cs")
             if not {"obj", "bin"}.intersection(p.relative_to(host).parts)]
    assert files, "Missing native Host sources"
    for path in files:
        assert path.name.lower() not in RETIRED_FILES, f"Retired production file: {path}"
        code = "\n".join(line for line in path.read_text(encoding="utf-8-sig").splitlines()
                         if not line.lstrip().startswith(("//", "/*", "*")))
        match = RETIRED_CODE.search(code)
        assert match is None, f"Retired production code: {path}: {match.group() if match else ''}"


def validate_compile_items(host, document):
    items = document["Items"]["Compile"]
    assert items, "Missing evaluated native Host Compile inputs"
    for item in items:
        path = Path(item["FullPath"]).resolve()
        assert path.is_relative_to(host.resolve()), f"Non-owned evaluated Compile input: {path}"
        assert path.name.lower() not in RETIRED_FILES, f"Retired Compile input: {path}"
        assert not item.get("Link"), f"Linked evaluated Compile input: {path}"


def validate_runtime(document):
    libraries = document.get("libraries")
    assert isinstance(libraries, dict) and libraries, "Missing compiled Host runtime library inventory"
    targets = document.get("targets")
    assert isinstance(targets, dict) and targets, "Missing compiled Host runtime targets"
    for label, entries in [("libraries", libraries), *targets.items()]:
        bad = sorted(name for name in entries
                     if ((p := name.split("/", 1)[0].lower()) in BLOCKED_EXACT
                         or any(p.startswith(prefix) for prefix in BLOCKED_PREFIXES)))
        assert not bad, f"Disallowed runtime dependency under {label}: {bad}"
    return len(libraries)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact", type=Path, default=ARTIFACT)
    parser.add_argument("--compile-inputs", type=Path)
    parser.add_argument("--source-only", action="store_true")
    args = parser.parse_args()
    validate_source(HOST)
    if args.compile_inputs:
        validate_compile_items(HOST, json.loads(args.compile_inputs.read_text(encoding="utf-8-sig")))
    if args.source_only:
        print("PASS VPS-10.7 source boundary (not runtime evidence)")
        return
    assert args.artifact.is_file(), f"No built .NET10 Host runtime manifest: {args.artifact}"
    count = validate_runtime(json.loads(args.artifact.read_text(encoding="utf-8-sig")))
    print(f"PASS VPS-04B/VPS-10.7: {count} runtime libraries; native-only Host source; "
          "zero Newtonsoft/OWIN/System.Web/Windows ACL dependencies")


if __name__ == "__main__":
    main()
