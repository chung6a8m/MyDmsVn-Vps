#!/usr/bin/env python3
"""Build a sanitized VPS-08 Linux event receipt from original evidence files.

The receipt proves structural coverage and byte hashes only.  It never claims
that a reboot, rollback, SQL reconciliation, or human review physically occurred.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from pathlib import Path
from typing import Any


EVENTS = (
    "environment-admission",
    "sql13-tls-oracle",
    "nginx-proxy",
    "systemd-audit",
    "restart-sigkill",
    "reboot-resume",
    "failed-deploy-rollback",
    "final-reconciliation",
)
SHA = re.compile(r"^[0-9a-f]{40}$")
DIGEST = re.compile(r"^[0-9a-f]{64}$")


class ReceiptBlocked(ValueError):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise ReceiptBlocked(code)


def strict_json(path: Path) -> Any:
    require(path.is_file() and not path.is_symlink(), "EvidenceFileInvalid")
    require(path.stat().st_size <= 1024 * 1024, "EvidenceFileTooLarge")

    def unique(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            require(key not in result, "DuplicateJsonMember:" + key)
            result[key] = value
        return result

    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)


def write_exclusive(path: Path, document: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(document, stream, indent=2, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
    except BaseException:
        try:
            path.unlink()
        except FileNotFoundError:
            pass
        raise


def replace_state(path: Path, document: dict[str, Any]) -> None:
    require(path.is_file() and not path.is_symlink(), "StateFileInvalid")
    temporary = path.with_name(path.name + ".next")
    if temporary.exists() or temporary.is_symlink():
        raise ReceiptBlocked("StateTemporaryExists")
    write_exclusive(temporary, document)
    os.replace(temporary, path)


def initialize(
    state_path: Path,
    source_sha: str,
    machine_sha256: str,
    artifact_sha256: str,
    operator: str,
) -> dict[str, Any]:
    require(SHA.fullmatch(source_sha) is not None, "SourceShaInvalid")
    require(DIGEST.fullmatch(machine_sha256) is not None, "MachineShaInvalid")
    require(DIGEST.fullmatch(artifact_sha256) is not None, "ArtifactShaInvalid")
    require(len(operator.strip()) >= 3, "OperatorMissing")
    document = {
        "schemaVersion": 1,
        "sourceSha": source_sha,
        "machineSha256": machine_sha256,
        "artifactSha256": artifact_sha256,
        "operator": operator.strip(),
        "events": [],
        "physicalEvidenceReview": "REQUIRED",
    }
    write_exclusive(state_path, document)
    return document


def record(state_path: Path, evidence_path: Path) -> dict[str, Any]:
    state = strict_json(state_path)
    evidence = strict_json(evidence_path)
    require(evidence.get("schemaVersion") == 1, "EventSchemaInvalid")
    event = evidence.get("event")
    require(event in EVENTS, "EventUnknown")
    require(not any(item.get("event") == event for item in state.get("events", [])),
            "EventDuplicate")
    require(evidence.get("sourceSha") == state.get("sourceSha"), "EventSourceMismatch")
    require(evidence.get("machineSha256") == state.get("machineSha256"),
            "EventMachineMismatch")
    require(evidence.get("operator") == state.get("operator"), "EventOperatorMismatch")
    require(isinstance(evidence.get("observedAtUtc"), str)
            and evidence["observedAtUtc"].endswith("Z"), "EventTimestampInvalid")
    require(DIGEST.fullmatch(str(evidence.get("evidenceDigest", ""))) is not None,
            "EventEvidenceDigestInvalid")
    expected_outcome = "EXPECTED_FAILURE_RECOVERED" if event == "failed-deploy-rollback" else "PASS"
    if event == "failed-deploy-rollback":
        require(evidence.get("outcome") == expected_outcome, "FailedDeployOutcomeInvalid")
    else:
        require(evidence.get("outcome") == expected_outcome, "EventOutcomeInvalid")
    raw = evidence_path.read_bytes()
    state["events"].append({
        "event": event,
        "observedAtUtc": evidence["observedAtUtc"],
        "outcome": evidence["outcome"],
        "evidenceDigest": evidence["evidenceDigest"],
        "evidenceFileSha256": hashlib.sha256(raw).hexdigest(),
        "evidenceFileBytes": len(raw),
    })
    replace_state(state_path, state)
    return state


def finalize(state_path: Path, output_path: Path) -> dict[str, Any]:
    state = strict_json(state_path)
    recorded = [item.get("event") for item in state.get("events", [])]
    require(len(recorded) == len(EVENTS) and set(recorded) == set(EVENTS),
            "EventCoverageGap")
    ordered = sorted(state["events"], key=lambda item: EVENTS.index(item["event"]))
    receipt = {
        "schemaVersion": 1,
        "sourceSha": state["sourceSha"],
        "machineSha256": state["machineSha256"],
        "artifactSha256": state["artifactSha256"],
        "operator": state["operator"],
        "events": ordered,
        "physicalEvidenceReview": "REQUIRED",
        "overall": "STRUCTURE_COMPLETE_NOT_REVIEWED",
    }
    write_exclusive(output_path, receipt)
    return receipt


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    init = commands.add_parser("init")
    init.add_argument("--state", required=True, type=Path)
    init.add_argument("--source-sha", required=True)
    init.add_argument("--machine-sha256", required=True)
    init.add_argument("--artifact-sha256", required=True)
    init.add_argument("--operator", required=True)
    add = commands.add_parser("record")
    add.add_argument("--state", required=True, type=Path)
    add.add_argument("--evidence", required=True, type=Path)
    done = commands.add_parser("finalize")
    done.add_argument("--state", required=True, type=Path)
    done.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.command == "init":
            initialize(args.state, args.source_sha, args.machine_sha256,
                       args.artifact_sha256, args.operator)
        elif args.command == "record":
            record(args.state, args.evidence)
        else:
            finalize(args.state, args.output)
        print("VPS09_LINUX_RECEIPT_STRUCTURE_PASS: physical review still REQUIRED")
        return 0
    except (ReceiptBlocked, OSError, ValueError, TypeError, KeyError) as error:
        print("VPS09_LINUX_RECEIPT_BLOCKED: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
