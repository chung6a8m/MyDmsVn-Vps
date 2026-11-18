#!/usr/bin/env python3
"""Create/verify deterministic, non-secret VPS-08 deployment-file manifests."""
from __future__ import annotations

import hashlib
import json
import re
import stat
import sys
from pathlib import Path

MANIFEST = "vps08-manifest.json"
NEEDED = {
    "MyDmsVn.Server.AspNetCoreHost.dll",
    "MyDmsVn.Server.AspNetCoreHost.deps.json",
    "MyDmsVn.Server.AspNetCoreHost.runtimeconfig.json",
}


def inventory(root: Path) -> dict[str, dict[str, str | int]]:
    if not root.is_dir() or root.is_symlink():
        raise ValueError("Artifact directory must be real.")
    if root.stat().st_mode & (stat.S_IWGRP | stat.S_IWOTH):
        raise ValueError("Group/world-writable artifact root refused.")
    files: dict[str, dict[str, str | int]] = {}
    for p in sorted(root.rglob("*")):
        rel = p.relative_to(root).as_posix()
        if p.is_symlink() or not (p.is_file() or p.is_dir()):
            raise ValueError("Symlinks and special files are forbidden.")
        entry_mode = p.lstat().st_mode
        if entry_mode & (stat.S_IWGRP | stat.S_IWOTH | stat.S_ISUID | stat.S_ISGID | stat.S_ISVTX):
            raise ValueError("Writable/special-mode artifact entry refused.")
        if p.is_dir():
            continue
        if any(part in ("", ".", "..") for part in rel.split("/")):
            raise ValueError("Unsafe artifact path.")
        if p.name == MANIFEST and p.parent == root:
            continue
        lower = p.name.lower()
        if lower.startswith(".env") or lower.endswith((".pfx", ".pem", ".key")):
            raise ValueError("Credential-shaped artifact is forbidden.")
        data = p.read_bytes()
        files[rel] = {"size": len(data), "sha256": hashlib.sha256(data).hexdigest()}
    if not NEEDED.issubset(files):
        raise ValueError("Published Host runtime is incomplete.")
    return files


def process(action: str, directory: str, sha: str) -> None:
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("A full 40-hex reviewed commit SHA is required.")
    root = Path(directory)
    files = inventory(root)
    file = root / MANIFEST
    if action == "create":
        if file.exists() or file.is_symlink():
            raise ValueError("Refusing to replace an existing artifact manifest.")
        result = {"format": "vps08-linux-v1", "source_sha": sha, "files": files}
        file.write_text(json.dumps(result, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    elif action == "verify":
        if not file.is_file() or file.is_symlink():
            raise ValueError("Missing artifact manifest.")
        result = json.loads(file.read_text(encoding="utf-8"))
        if set(result) != {"format", "source_sha", "files"} or result["format"] != "vps08-linux-v1" or result["source_sha"] != sha:
            raise ValueError("Artifact source SHA or format mismatch.")
        if result["files"] != files:
            raise ValueError("Artifact content or file inventory mismatch.")
    else:
        raise ValueError("action must be create or verify")
    print(f"VPS08_ARTIFACT_{action.upper()}_PASS: source_sha={sha}; files={len(files)}")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 4:
            raise ValueError("Usage: vps08-manifest.py create|verify ARTIFACT_DIRECTORY EXACT_SHA")
        process(sys.argv[1], sys.argv[2], sys.argv[3])
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as exc:
        print(f"VPS08_ARTIFACT_BLOCKED: {type(exc).__name__}", file=sys.stderr)
        sys.exit(1)
