#!/usr/bin/env python3
"""VPS-00B repository foundation guard. Static validation only; never a build."""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parent.parent
REQUIRED_DOCS = (
    "README.md", "AGENTS.md", "AI_CONTEXT.md", "global.json",
    "docs/import/README.md", "docs/import/sql-checksums.sha256",
    "docs/roadmap/README.md", "docs/roadmap/vps-00-vps-09.md",
    "docs/decisions/README.md",
    "docs/decisions/OWN-VPS-01.md", "docs/decisions/OWN-VPS-02.md",
    "docs/decisions/OWN-VPS-03.md",
    "docs/architecture/overview.md",
    "docs/architecture/dependency-boundaries.md",
    "docs/architecture/desktop-http-architecture.md",
    "docs/architecture/linux-hosting.md",
    "docs/build/source-inventory.md", "docs/build/build-and-ci.md",
    "docs/testing/test-policy.md", "docs/testing/acceptance-matrix.md",
    "docs/contracts/http-api-parity.md",
    "docs/contracts/json-wire-compatibility.md",
    "docs/migration/dbup-to-fluentmigrator.md",
    "docs/evidence/vps-00b-baseline.md",
    "docs/contracts/README.md", "docs/contracts/corpus-blobs.sha1",
    "docs/SECURITY_ADMINISTRATION_RUNBOOK.md",
    "tests/README.md", ".github/workflows/vps-foundation.yml",
    "vendor/MyDmsVn.WinFormsKit/THIRD_PARTY_NOTICES.md",
    "vendor/MyDmsVn.WinFormsKit/vendor/sourcegrid/SourceGrid/SourceGrid.csproj",
)
LINK_PATTERN = re.compile(r"(?<!!)\[[^]]+\]\(([^)]+)\)")
SLN_PROJECT_PATTERN = re.compile(
    r'^Project\("[^"]+"\) = "[^"]+", "([^"]+\.csproj)"', re.MULTILINE
)
ABSOLUTE_PATTERN = re.compile(r"^(?:[a-zA-Z]:[/\\]|/|\\\\|https?://)")
SQL_PATTERN = re.compile(r"^src/MyDmsVn\.Server\.DbMigrator/Scripts/\d{3}_.+\.sql$")


def tracked_files() -> tuple[dict[str, str], dict[str, str], list[str]]:
    """Return path->git stage mode, plus errors from Git index inspection."""
    p = subprocess.run(
        ["git", "ls-files", "--stage", "-z"], cwd=ROOT,
        capture_output=True, check=False,
    )
    if p.returncode:
        return {}, {}, ["git ls-files failed: " + p.stderr.decode("utf-8", "replace")]
    modes: dict[str, str] = {}
    blob_shas: dict[str, str] = {}
    errors: list[str] = []
    for record in p.stdout.decode("utf-8", "replace").split("\0"):
        if not record:
            continue
        try:
            metadata, path = record.split("\t", 1)
            mode, sha, stage = metadata.split()
        except ValueError:
            errors.append("Unparseable git index record")
            continue
        if stage != "0":
            errors.append("Unmerged index entry: " + path)
        modes[path] = mode
        blob_shas[path] = sha
        if mode == "160000":
            errors.append("Forbidden Git submodule/gitlink: " + path)
    return modes, blob_shas, errors


def safe_relative_target(parent: Path, raw: str, description: str,
                         errors: list[str]) -> None:
    if "$(" in raw or "@(" in raw or "%(" in raw:
        errors.append(description + ": dynamic in-tree reference: " + raw)
        return
    if ABSOLUTE_PATTERN.match(raw):
        errors.append(description + ": external/absolute reference: " + raw)
        return
    target = (parent / raw.replace("\\", "/")).resolve()
    if not target.is_relative_to(ROOT):
        errors.append(description + ": escaped repository root: " + raw)
    elif not target.is_file():
        errors.append(description + ": missing in-tree file: " + raw)


def validate_project_references(project: str, errors: list[str]) -> int:
    path = ROOT / project
    try:
        xml = ET.parse(path)
    except (ET.ParseError, OSError) as exc:
        errors.append("Cannot parse project " + project + ": " + str(exc))
        return 0
    count = 0
    for node in xml.iter():
        tag = node.tag.rsplit("}", 1)[-1]
        if tag == "ProjectReference":
            count += 1
            raw = node.get("Include")
            if not raw:
                errors.append(project + ": ProjectReference missing Include")
            else:
                safe_relative_target(path.parent, raw, project, errors)
        elif tag == "Import":
            raw = node.get("Project")
            # Property-based imports are resolved by MSBuild during restore/build.
            # Literal paths must remain entirely inside this repository.
            if raw and not any(token in raw for token in ("$(", "@(", "%(")):
                safe_relative_target(path.parent, raw, project + " MSBuild Import", errors)
        elif tag == "HintPath" and node.text:
            raw = node.text.strip()
            if raw and not any(token in raw for token in ("$(", "@(", "%(")):
                safe_relative_target(path.parent, raw, project + " HintPath", errors)
    return count


def validate_solution(path: str, errors: list[str]) -> int:
    data = (ROOT / path).read_text(encoding="utf-8-sig", errors="replace")
    refs = SLN_PROJECT_PATTERN.findall(data)
    for ref in refs:
        safe_relative_target((ROOT / path).parent, ref, path, errors)
    if not refs:
        errors.append("No .csproj projects found in solution " + path)
    return len(refs)


def validate_links(path: str, tracked: set[str], errors: list[str]) -> int:
    data = (ROOT / path).read_text(encoding="utf-8-sig", errors="replace")
    count = 0
    for match in LINK_PATTERN.finditer(data):
        href = match.group(1).strip().split(" ", 1)[0].strip("<>")
        if href.startswith(("https://", "http://", "mailto:", "data:", "#")):
            continue
        href = unquote(href.split("#", 1)[0].split("?", 1)[0])
        if not href:
            continue
        target = ((ROOT / path).parent / href).resolve()
        count += 1
        if not target.is_relative_to(ROOT) or not target.exists():
            errors.append(path + ": broken relative markdown link: " + href)
    return count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", default="foundation-report.json")
    parser.add_argument("--expected-projects", type=int, default=55)
    args = parser.parse_args()
    errors: list[str] = []
    index, blob_shas, index_errors = tracked_files()
    errors.extend(index_errors)
    tracked = set(index)

    for doc in REQUIRED_DOCS:
        if doc not in tracked or not (ROOT / doc).is_file():
            errors.append("Missing required tracked document/file: " + doc)

    if any(p.endswith("/.gitmodules") or p == ".gitmodules" for p in tracked):
        errors.append("Tracked .gitmodules is forbidden: vendor trees must be local")
    if any(p.endswith("/.git/HEAD") or p == ".git/HEAD" for p in tracked):
        errors.append("Nested Git repository metadata is forbidden")

    projects = sorted(p for p in tracked if p.endswith(".csproj"))
    # VPS-04A adds exactly one CI-only executable outside product src graph.
    # It source-links the original migrations; it is not a production dependency.
    ci_sql_project = "tests/MyDmsVn.Vps04a.SqlBootstrap/MyDmsVn.Vps04a.SqlBootstrap.csproj"
    if ci_sql_project not in projects:
        errors.append("Missing exact test-only SQL13 DbUp bootstrap project")
    # VPS-06A explicitly adds ONE isolated net10 canary project. This does not
    # authorize arbitrary project-count drift or a production graph reference.
    canary = "tests/MyDmsVn.Vps06A.MediatorCanary/MyDmsVn.Vps06A.MediatorCanary.csproj"
    if canary not in projects:
        errors.append("Missing approved VPS-06A isolated generator canary project")
    elif "ProjectReference" in (ROOT / canary).read_text(encoding="utf-8-sig"):
        errors.append("VPS-06A canary may not reference production projects")
    # VPS-07A adds an isolated net10 FluentMigrator source assembly and one
    # explicit non-xUnit contract executable; neither joins legacy net48 graphs.
    fm_library = "src/MyDmsVn.Server.FluentMigrator/MyDmsVn.Server.FluentMigrator.csproj"
    fm_contract = "tests/MyDmsVn.Vps07a.ContractTests/MyDmsVn.Vps07a.ContractTests.csproj"
    if fm_library not in projects or fm_contract not in projects:
        errors.append("Missing exact VPS-07A library/contract project pair")
    fm_cli = "src/MyDmsVn.Server.FluentMigrator.Cli/MyDmsVn.Server.FluentMigrator.Cli.csproj"
    if fm_cli not in projects:
        errors.append("Missing approved VPS-07B standalone migration CLI project")
    elif "net10.0" not in (ROOT / fm_cli).read_text(encoding="utf-8-sig"):
        errors.append("VPS-07B CLI must target net10.0")
    if len(projects) != args.expected_projects:
        errors.append(
            f"Project count drift: expected {args.expected_projects}; "
            f"tracked {len(projects)}. Re-inventory intentionally when changing scope."
        )
    reference_count = sum(validate_project_references(p, errors) for p in projects)
    solutions = sorted(p for p in tracked if p.endswith(".sln"))
    solution_count = sum(validate_solution(p, errors) for p in solutions)

    fixture_dirs = {
        "web2-v1": 34,
        "web3-v1": 38,
        "web4-v1": 48,
        "web5-resolution-v1": 41,
        "web6-resend-eligibility-v1": 17,
    }
    for corpus, expected in fixture_dirs.items():
        prefix = "docs/contracts/" + corpus + "/"
        actual = sum(1 for p in tracked if p.startswith(prefix))
        if actual != expected:
            errors.append(f"Fixture corpus {corpus}: expected {expected} files, got {actual}")

    # Freeze WEB2-WEB6 technical corpora by Git blob identity, not only file count.
    # The manifest itself is version-controlled: intentional changes require a
    # visibly reviewed manifest diff as well as updated contract tests.
    pinned_corpus: dict[str, str] = {}
    try:
        for number, line in enumerate(
            (ROOT / "docs/contracts/corpus-blobs.sha1").read_text(
                encoding="utf-8"
            ).splitlines(), 1
        ):
            match = re.fullmatch(r"([0-9a-f]{40})  (docs/contracts/[^ ]+)", line)
            if not match:
                errors.append(f"Invalid corpus blob manifest line {number}")
                continue
            sha, corpus_path = match.groups()
            if not any(corpus_path.startswith(f"docs/contracts/{folder}/")
                       for folder in fixture_dirs):
                errors.append("Manifest references non-corpus path: " + corpus_path)
            if corpus_path in pinned_corpus:
                errors.append("Duplicate corpus manifest path: " + corpus_path)
            pinned_corpus[corpus_path] = sha
            if blob_shas.get(corpus_path) != sha:
                errors.append("Frozen contract corpus Git blob drift: " + corpus_path)
    except OSError as exc:
        errors.append("Cannot read corpus blob manifest: " + str(exc))
    tracked_corpus = {
        path for path in tracked
        if any(path.startswith(f"docs/contracts/{folder}/")
               for folder in fixture_dirs)
    }
    if set(pinned_corpus) != tracked_corpus:
        errors.append(
            "Corpus manifest path mismatch (missing/unexpected): "
            + str(sorted(tracked_corpus.symmetric_difference(pinned_corpus)))
        )

    sql_scripts = sorted(p for p in tracked if SQL_PATTERN.match(p))
    if len(sql_scripts) != 13:
        errors.append(f"Historical SQL script count: expected 13; got {len(sql_scripts)}")
    try:
        sdk = json.loads((ROOT / "global.json").read_text(encoding="utf-8-sig"))
        if sdk.get("sdk", {}).get("version") != "8.0.400":
            errors.append("VPS-00B baseline SDK is not 8.0.400; re-evaluate phase.")
    except (OSError, ValueError) as exc:
        errors.append("Cannot read global.json: " + str(exc))

    # Only newly authored governance docs are checked for links. Imported
    # vendor/source docs can have historical attribution and missing old anchors.
    governance = sorted(p for p in tracked if p.startswith("docs/")
                        and p.endswith(".md") and not p.startswith(("docs/import/", "docs/contracts/web")))
    governance += ["README.md", "AGENTS.md", "tests/README.md"]
    checked_links = 0
    for p in governance:
        if p in tracked:
            checked_links += validate_links(p, tracked, errors)

    report = {
        "gate": "VPS-00B static foundation (NOT a build, test or Linux runtime)",
        "status": "PASS" if not errors else "FAIL",
        "counts": {
            "tracked_files": len(tracked),
            "csproj_projects": len(projects),
            "project_references": reference_count,
            "solutions": len(solutions),
            "solution_project_entries": solution_count,
            "historical_sql_scripts": len(sql_scripts),
            "frozen_corpus_blobs": len(pinned_corpus),
            "governance_relative_links": checked_links,
        },
        "errors": errors,
    }
    target = Path(args.report)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
