#!/usr/bin/env python3
"""VPS-04B: inspect the built Linux host artifact, not merely source imports.

Source-only compatibility namespaces System.Web.Http/Microsoft.Owin do not
imply their legacy assemblies are runtime dependencies. Reject actual
runtime packages and Windows ACL source accidentally linked by the host.
"""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "src" / "MyDmsVn.Server.AspNetCoreHost"
ARTIFACT = HOST / "bin" / "Release" / "net10.0" / "MyDmsVn.Server.AspNetCoreHost.deps.json"
PROJECT = HOST / "MyDmsVn.Server.AspNetCoreHost.csproj"

assert ARTIFACT.is_file(), f"No built .NET10 host runtime manifest: {ARTIFACT.name}"
document = json.loads(ARTIFACT.read_text(encoding="utf-8"))
libraries = document.get("libraries")
assert isinstance(libraries, dict) and libraries, "Missing compiled host runtime library inventory"

# Deliberately inspect dependency names from the produced runtime manifest,
# not source namespace usage. Legacy namespace facades remain until a
# separately reviewable controller migration.
blocked_exact = {
    "system.web", "system.security.accesscontrol",
    "system.security.principal.windows", "microsoft.aspnetcore.owin",
    "owin",
}
blocked_prefixes = ("microsoft.owin", "microsoft.aspnet.webapi", "system.web.")
bad = sorted(
    name for name in libraries
    if ((p := name.split("/", 1)[0].lower()) in blocked_exact
        or any(p.startswith(prefix) for prefix in blocked_prefixes))
)
assert not bad, f"Disallowed runtime dependency in net10 Linux host: {bad}"

root = ET.parse(PROJECT).getroot()
linked = [
    element for element in root.findall(".//Compile")
    if "MyDmsVn.Server.HttpHost/*.cs" in element.attrib.get("Include", "")
]
assert len(linked) == 1, "Unexpected linked legacy HTTP source scope"
excluded = linked[0].attrib.get("Exclude", "").split(";")
for filename in (
    "Startup.cs", "Web2Pipeline.cs", "ProtectedFileSecurityAuditSink.cs",
    "SecurityAuditReadiness.cs",
):
    assert any(entry.endswith("/" + filename) for entry in excluded), (
        f"Legacy OWIN/Windows ACL source linked into Linux host: {filename}"
    )
print(
    f"PASS VPS-04B: {len(libraries)} net10 host runtime libraries, "
    "zero OWIN/System.Web/Windows ACL dependencies; Windows-only "
    "legacy host source excluded"
)
