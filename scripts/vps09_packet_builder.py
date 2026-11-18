#!/usr/bin/env python3
"""Hash selected evidence files into an unsigned VPS-09 packet index.

This tool deliberately cannot create an Owner release decision or signature.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from pathlib import Path, PurePosixPath
from typing import Any


SHA = re.compile(r"^[0-9a-f]{40}$")


class PacketBlocked(ValueError):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise PacketBlocked(code)


def build_index(root: Path, source_sha: str, artifacts: list[str]) -> dict[str, Any]:
    root = root.resolve()
    require(root.is_dir() and not root.is_symlink(), "EvidenceRootInvalid")
    require(SHA.fullmatch(source_sha) is not None, "SourceShaInvalid")
    require(bool(artifacts), "ArtifactListEmpty")
    seen: set[str] = set()
    ledger: dict[str, dict[str, Any]] = {}
    for name in artifacts:
        relative = PurePosixPath(name)
        require(not relative.is_absolute() and ".." not in relative.parts
                and "\\" not in name and str(relative) == name,
                "ArtifactPathInvalid")
        require(name not in seen, "ArtifactDuplicate")
        seen.add(name)
        path = root.joinpath(*relative.parts)
        require(path.is_file() and not path.is_symlink(), "ArtifactInvalid:" + name)
        resolved = path.resolve()
        try:
            resolved.relative_to(root)
        except ValueError as error:
            raise PacketBlocked("ArtifactPathInvalid") from error
        data = path.read_bytes()
        require(len(data) <= 50 * 1024 * 1024, "ArtifactTooLarge:" + name)
        ledger[name] = {
            "sha256": hashlib.sha256(data).hexdigest(),
            "bytes": len(data),
        }
    return {
        "schemaVersion": 1,
        "sourceSha": source_sha,
        "artifacts": dict(sorted(ledger.items())),
        "signingStatus": "UNSIGNED",
        "ownerReleaseDecision": "NOT_ATTACHED",
        "nextStep": "Independent reviewer attaches the explicit Owner decision before offline Ed25519 signing.",
    }


def write_exclusive(path: Path, document: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(document, stream, indent=2, sort_keys=True)
        stream.write("\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--artifact", action="append", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        write_exclusive(args.output, build_index(args.root, args.source_sha, args.artifact))
        print("VPS09_PACKET_INDEX_PASS: UNSIGNED; Owner release decision NOT_ATTACHED")
        return 0
    except (PacketBlocked, OSError, ValueError, TypeError) as error:
        print("VPS09_PACKET_INDEX_BLOCKED: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
