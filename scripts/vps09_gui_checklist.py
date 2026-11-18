#!/usr/bin/env python3
"""Create and structurally validate the nine attended Windows GUI checkpoints."""

from __future__ import annotations

import argparse
import json
import os
import re
from pathlib import Path
from typing import Any


CHECKPOINTS = (
    ("startup", 100),
    ("login-session", 100),
    ("permissions", 100),
    ("sales-workflow", 100),
    ("inventory-workflow", 100),
    ("startup", 125),
    ("login-session", 125),
    ("unknown-recovery", 125),
    ("logout", 125),
)
SHA = re.compile(r"^[0-9a-f]{40}$")
DIGEST = re.compile(r"^[0-9a-f]{64}$")


class ChecklistBlocked(ValueError):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise ChecklistBlocked(code)


def load(path: Path) -> dict[str, Any]:
    require(path.is_file() and not path.is_symlink(), "ChecklistFileInvalid")

    def unique(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            require(key not in result, "DuplicateJsonMember:" + key)
            result[key] = value
        return result

    document = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)
    require(isinstance(document, dict), "ChecklistObjectRequired")
    return document


def validate(path: Path) -> dict[str, Any]:
    document = load(path)
    require(document.get("schemaVersion") == 2, "ChecklistSchemaInvalid")
    require(document.get("governanceMode") == "single-owner-exception"
            and document.get("decisionId") == "OWN-VPS-04",
            "ChecklistGovernanceInvalid")
    require(SHA.fullmatch(str(document.get("sourceSha", ""))) is not None,
            "ChecklistSourceShaInvalid")
    require(DIGEST.fullmatch(str(document.get("desktopExecutableSha256", ""))) is not None,
            "DesktopDigestInvalid")
    owner = str(document.get("owner", "")).strip()
    require(owner == "chung6a8m", "ChecklistOwnerInvalid")
    require(document.get("sessionType") == "interactive", "InteractiveSessionRequired")
    rows = document.get("checkpoints")
    require(isinstance(rows, list) and len(rows) == len(CHECKPOINTS),
            "CheckpointCoverageGap")
    seen: set[tuple[str, int]] = set()
    evidence: set[str] = set()
    for row in rows:
        require(isinstance(row, dict), "CheckpointObjectRequired")
        pair = (row.get("id"), row.get("scalePercent"))
        require(pair in CHECKPOINTS and pair not in seen, "CheckpointCoverageGap")
        seen.add(pair)
        require(row.get("outcome") == "PASS", "CheckpointNotPassed")
        require(isinstance(row.get("observedAtUtc"), str)
                and row["observedAtUtc"].endswith("Z"), "CheckpointTimestampInvalid")
        digest = str(row.get("evidenceSha256", ""))
        require(DIGEST.fullmatch(digest) is not None, "CheckpointEvidenceInvalid")
        require(digest not in evidence, "CheckpointEvidenceReused")
        evidence.add(digest)
    require(seen == set(CHECKPOINTS), "CheckpointCoverageGap")
    return {
        "schemaVersion": 2,
        "sourceSha": document["sourceSha"],
        "desktopExecutableSha256": document["desktopExecutableSha256"],
        "governanceMode": "single-owner-exception",
        "decisionId": "OWN-VPS-04",
        "owner": owner,
        "executed": 9,
        "passed": 9,
        "failed": 0,
        "skipped": 0,
        "attendedEvidenceReview": "OWNER_SECOND_PASS_REQUIRED",
        "overall": "STRUCTURE_COMPLETE_NOT_REVIEWED",
    }


def template(source_sha: str, desktop_sha256: str, owner: str) -> dict[str, Any]:
    require(SHA.fullmatch(source_sha) is not None, "ChecklistSourceShaInvalid")
    require(DIGEST.fullmatch(desktop_sha256) is not None, "DesktopDigestInvalid")
    require(owner == "chung6a8m", "ChecklistOwnerInvalid")
    return {
        "schemaVersion": 2,
        "sourceSha": source_sha,
        "desktopExecutableSha256": desktop_sha256,
        "governanceMode": "single-owner-exception",
        "decisionId": "OWN-VPS-04",
        "owner": owner,
        "sessionType": "interactive",
        "checkpoints": [
            {"id": checkpoint, "scalePercent": scale, "outcome": "NOT_RECORDED",
             "observedAtUtc": "", "evidenceSha256": ""}
            for checkpoint, scale in CHECKPOINTS
        ],
    }


def write_exclusive(path: Path, document: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(document, stream, indent=2, sort_keys=True)
        stream.write("\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    create = commands.add_parser("template")
    create.add_argument("--source-sha", required=True)
    create.add_argument("--desktop-sha256", required=True)
    create.add_argument("--owner", required=True)
    create.add_argument("--output", required=True, type=Path)
    check = commands.add_parser("validate")
    check.add_argument("--input", required=True, type=Path)
    check.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.command == "template":
            write_exclusive(args.output, template(args.source_sha, args.desktop_sha256,
                                                  args.owner))
            print("VPS09_GUI_TEMPLATE_CREATED: all nine checkpoints remain NOT_RECORDED")
        else:
            write_exclusive(args.output, validate(args.input))
            print("VPS09_GUI_CHECKLIST_STRUCTURE_PASS: Owner second-pass review still REQUIRED; "
                  "NO_INDEPENDENT_HUMAN_REVIEW")
        return 0
    except (ChecklistBlocked, OSError, ValueError, TypeError, KeyError) as error:
        print("VPS09_GUI_CHECKLIST_BLOCKED: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
