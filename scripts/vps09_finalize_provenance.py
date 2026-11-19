#!/usr/bin/env python3
"""Validate OWN-VPS-04 finalize job provenance and real 24-hour cooling.

This *online* check complements, and must follow, vps09_verify.py's offline
Ed25519 packet verification. It cannot prove physical SQL, VPS or GUI outcomes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

REPO = "chung6a8m/MyDmsVn-Vps"
OWNER = "chung6a8m"
SHA40 = re.compile(r"^[a-f0-9]{40}$")
RUN_URL = re.compile(
    r"^https://github\.com/chung6a8m/MyDmsVn-Vps/actions/runs/([1-9][0-9]*)$"
)
UTC = re.compile(r"^20[0-9]{2}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")
FINALIZE_JOB = "Validate attended GUI, Linux receipt and cleanup (no release approval)"
FINALIZE_WORKFLOW = ".github/workflows/vps-09-evidence.yml"
COOLING = timedelta(hours=24)


def require(ok: bool, code: str) -> None:
    if not ok:
        raise ValueError("VPS09." + code)


def parse_time(raw: Any, code: str) -> datetime:
    require(isinstance(raw, str) and UTC.fullmatch(raw) is not None, code)
    try:
        return datetime.strptime(raw, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
    except ValueError as error:
        raise ValueError("VPS09." + code) from error


def fetch_github(path: str, token: str) -> dict[str, Any]:
    # URL path is formed solely from the numeric run ID, not untrusted URLs.
    request = Request("https://api.github.com" + path, headers={
        "Accept": "application/vnd.github+json",
        "Authorization": "Bearer " + token,
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "mydmsvn-vps09-finalize-provenance",
    })
    with urlopen(request, timeout=20) as response:
        raw = response.read(1024 * 1024 + 1)
    require(len(raw) <= 1024 * 1024, "GitHubApiPayloadTooLarge")
    value = json.loads(raw)
    require(isinstance(value, dict), "GitHubApiShapeInvalid")
    return value


def verify_provenance(
    packet: Path, expected_sha: str, token: str,
    *, fetch: Callable[[str, str], dict[str, Any]] = fetch_github,
    now: datetime | None = None,
) -> None:
    require(SHA40.fullmatch(expected_sha) is not None, "ExpectedShaInvalid")
    require(bool(token and token.strip()), "GitHubReadTokenMissing")
    require(packet.is_dir() and not packet.is_symlink(), "PacketDirectoryInvalid")
    att = json.loads((packet / "attestation.json").read_text(encoding="utf-8"))
    require(isinstance(att, dict) and att.get("schemaVersion") == 2
            and att.get("headSha") == expected_sha, "ProvenanceCandidateMismatch")
    require(att.get("finalizationReceipt") == "finalization-receipt.json",
            "FinalizationReceiptPathInvalid")
    receipt_bytes = (packet / "finalization-receipt.json").read_bytes()
    ledger = att.get("artifacts", {}).get("finalization-receipt.json", {})
    require(isinstance(ledger, dict)
            and ledger.get("bytes") == len(receipt_bytes)
            and ledger.get("sha256") == hashlib.sha256(receipt_bytes).hexdigest(),
            "FinalizationReceiptDigestMismatch")
    receipt = json.loads(receipt_bytes)
    match = RUN_URL.fullmatch(str(receipt.get("workflowRunUrl", "")))
    require(match is not None, "FinalizationWorkflowRunInvalid")
    run_id = match.group(1)
    run = fetch(f"/repos/{REPO}/actions/runs/{run_id}", token)
    require(run.get("id") == int(run_id)
            and run.get("path") == FINALIZE_WORKFLOW
            and run.get("head_sha") == expected_sha
            and run.get("head_branch") == "main", "ProtectedFinalizeRunIdentityMismatch")
    require(run.get("event") == "workflow_dispatch"
            and (run.get("actor") or {}).get("login") == OWNER
            and (run.get("triggering_actor") or {}).get("login") == OWNER,
            "ProtectedFinalizeActorMismatch")
    require(run.get("status") == "completed" and run.get("conclusion") == "success",
            "ProtectedFinalizeRunNotSuccessful")
    jobs = fetch(f"/repos/{REPO}/actions/runs/{run_id}/jobs?per_page=100", token)
    rows = jobs.get("jobs")
    require(isinstance(rows, list) and jobs.get("total_count") == len(rows),
            "ProtectedFinalizeJobsIncomplete")
    finalize = [job for job in rows if job.get("name") == FINALIZE_JOB]
    require(len(finalize) == 1 and finalize[0].get("status") == "completed"
            and finalize[0].get("conclusion") == "success",
            "ProtectedFinalizeJobMissingOrFailed")
    started = parse_time(finalize[0].get("started_at"), "ProtectedFinalizeStartInvalid")
    completed = parse_time(finalize[0].get("completed_at"), "ProtectedFinalizeEndInvalid")
    finalized = parse_time(receipt.get("finalizedAtUtc"), "FinalizationTimestampInvalid")
    require(started <= finalized <= completed, "FinalizationTimestampOutsideProtectedJob")
    require(started >= datetime(2026, 11, 19, tzinfo=timezone.utc),
            "ProtectedFinalizeBeforeOwnerDecision")
    approved = parse_time((att.get("releaseDecision") or {}).get("approvedAtUtc"),
                          "OwnerApprovalTimestampInvalid")
    current = now if now is not None else datetime.now(timezone.utc)
    require(approved <= current, "OwnerApprovalInFuture")
    require(approved >= completed + COOLING, "ApprovalPrecedesProtectedCoolingOff")
    require(current >= completed + COOLING, "ProtectedCoolingOffIncomplete")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("verify", choices=("verify",))
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--expected-sha", required=True)
    args = parser.parse_args()
    try:
        verify_provenance(args.bundle, args.expected_sha,
                          os.environ.get("VPS09_GITHUB_READ_TOKEN", ""))
        print("VPS09_FINALIZE_PROVENANCE_PASS: exact protected GitHub finalize job "
              "and 24h from actual job completion verified; physical evidence remains "
              "Owner-reviewed only; NO_INDEPENDENT_HUMAN_REVIEW")
        return 0
    except (OSError, ValueError, TypeError, KeyError, HTTPError, URLError) as error:
        print("VPS09_FINALIZE_PROVENANCE_BLOCKED: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
