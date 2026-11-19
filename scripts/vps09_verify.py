#!/usr/bin/env python3
"""VPS-09: fail-closed signed *evidence* verifier, not an E2E runner.

A valid signature binds the named signer's assertions to file digests, not the
truth of physical SQL/VPS/GUI operations. OWN-VPS-04 permits a narrowly scoped
single-Owner closeout with explicit reduced assurance and compensating controls.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from datetime import datetime, timedelta, timezone
from tempfile import TemporaryDirectory
from pathlib import Path, PurePosixPath
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
SPEC_PATH = ROOT / "docs/testing/vps-09-gates.json"
SHA40 = re.compile(r"^[a-f0-9]{40}$")
SHA256 = re.compile(r"^[a-f0-9]{64}$")
RUN_URL = re.compile(
    r"^https://github\.com/chung6a8m/MyDmsVn-Vps/actions/runs/[1-9][0-9]*$"
)
MAX_ARTIFACT_BYTES = 8 * 1024 * 1024
UTC_TIMESTAMP = re.compile(r"^20[0-9]{2}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")
DECISION_EFFECTIVE_UTC = datetime(2026, 11, 19, tzinfo=timezone.utc)
ASSURANCE = "NO_INDEPENDENT_HUMAN_REVIEW"
LINUX_EVENTS = {
    "environment-admission", "sql13-tls-oracle", "nginx-proxy", "systemd-audit",
    "restart-sigkill", "reboot-resume", "failed-deploy-rollback", "final-reconciliation",
}
GUI_CHECKPOINTS = {
    ("startup", 100), ("login-session", 100), ("permissions", 100),
    ("sales-workflow", 100), ("inventory-workflow", 100), ("startup", 125),
    ("login-session", 125), ("unknown-recovery", 125), ("logout", 125),
}


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError("VPS09." + message)


def now_utc() -> datetime:
    return datetime.now(timezone.utc)


def reject_duplicate_members(members: list[tuple[str, Any]]) -> dict[str, Any]:
    obj: dict[str, Any] = {}
    for name, value in members:
        require(name not in obj, "DuplicateJsonMember:" + name)
        obj[name] = value
    return obj


def reject_nonfinite_number(value: str) -> None:
    raise ValueError("VPS09.NonFiniteJsonNumber:" + value)


def parse_json(raw: str) -> Any:
    return json.loads(raw, object_pairs_hook=reject_duplicate_members,
                      parse_constant=reject_nonfinite_number)


def document(path: Path) -> Any:
    return parse_json(path.read_text(encoding="utf-8"))


def inventory() -> tuple[dict[str, Any], set[tuple[str, str]], set[tuple[str, str]]]:
    spec = document(SPEC_PATH)
    require(spec.get("schemaVersion") == 1, "GateRegistryVersion")
    routes: list[tuple[str, str]] = []
    for source in spec["sourceInventories"]:
        for item in document(ROOT / source)["endpoints"]:
            routes.append((item["httpMethod"], item["path"]))
    route_set = set(routes)
    require(len(route_set) == len(routes) == spec["requiredRoutes"] == 57,
            "RouteInventoryMustContain57UniqueEntries")
    observations = document(ROOT / spec["frozenObservations"])
    pairs = [(row["CaseId"], row["Variant"]) for row in observations]
    pair_set = set(pairs)
    require(len(pair_set) == len(pairs) == spec["requiredLiveVariants"] == 293,
            "ObservationInventoryMustContain293UniquePairs")
    require(len({case for case, _ in pair_set}) == len(route_set),
            "ObservationIdsMustCoverEveryRoute")
    ids = [row["id"] for row in spec["gates"]]
    require(len(ids) == len(set(ids)) == 14, "GateRegistryDuplicateOrMissing")
    require(len(spec["oracleScenarios"]) == len(set(spec["oracleScenarios"])),
            "OracleScenariosDuplicate")
    require(len(spec["requiredWorkflowFiles"]) == len(set(spec["requiredWorkflowFiles"])),
            "RequiredWorkflowsDuplicate")
    return spec, route_set, pair_set


def sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def checked_relative(value: str) -> PurePosixPath:
    require(isinstance(value, str) and bool(value) and "\\" not in value,
            "UnsafeArtifactPath")
    path = PurePosixPath(value)
    require(not path.is_absolute() and all(part not in ("", ".", "..") for part in value.split("/"))
            and str(path) == value, "UnsafeArtifactPath")
    require(not any(part.startswith(".") for part in path.parts), "HiddenArtifactPath")
    return path


def validate_artifacts(folder: Path, attestation: dict[str, Any]) -> set[str]:
    require(folder.is_dir() and not folder.is_symlink(), "BundleDirectoryUnsafe")
    listed = attestation.get("artifacts")
    require(isinstance(listed, dict) and len(listed) >= 3, "MissingArtifactLedger")
    for name, record in listed.items():
        relative = checked_relative(name)
        target = folder
        for part in relative.parts:
            target = target / part
            require(not target.is_symlink(), "SymlinkArtifactRefused")
        require(target.is_file(), "MissingEvidenceArtifact")
        require(isinstance(record, dict), "ArtifactLedgerMalformed")
        size = target.stat().st_size
        require(0 < size <= MAX_ARTIFACT_BYTES and record.get("bytes") == size,
                "ArtifactSizeMismatch")
        require(SHA256.fullmatch(str(record.get("sha256", ""))) is not None
                and sha256(target.read_bytes()) == record["sha256"],
                "ArtifactDigestMismatch")
    existing: set[str] = set()
    for target in folder.rglob("*"):
        require(not target.is_symlink(), "UnlistedSymlinkRefused")
        if target.is_file():
            existing.add(target.relative_to(folder).as_posix())
        else:
            require(target.is_dir(), "SpecialArtifactRefused")
    require(existing == set(listed) | {"attestation.json", "attestation.sig"},
            "UnlistedOrMissingArtifact")
    return set(listed)


def verified_artifact_document(folder: Path, attestation: dict[str, Any], name: str) -> Any:
    # validate_artifacts() checks the whole packet, but an external operator
    # can still change an evidence file before observations/oracles are read.
    # Parse only bytes whose digest is bound to the signed ledger.
    require(name in attestation["artifacts"], "ArtifactNotListed")
    target = folder / checked_relative(name)
    record = attestation["artifacts"][name]
    require(target.is_file() and not target.is_symlink()
            and target.stat().st_size <= MAX_ARTIFACT_BYTES,
            "ArtifactChangedAfterLedgerVerification")
    raw = target.read_bytes()
    require(len(raw) == record["bytes"] and sha256(raw) == record["sha256"],
            "ArtifactChangedAfterLedgerVerification")
    return parse_json(raw.decode("utf-8"))


def utc_timestamp(value: Any, code: str) -> datetime:
    raw = str(value)
    require(UTC_TIMESTAMP.fullmatch(raw) is not None, code)
    try:
        return datetime.strptime(raw, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
    except ValueError as error:
        raise ValueError("VPS09." + code) from error


def exact_fields(value: Any, expected: set[str], code: str) -> dict[str, Any]:
    require(isinstance(value, dict) and set(value) == expected, code)
    return value


def validate_finalization(folder: Path, att: dict[str, Any], expected_sha: str,
                          owner: str, first_runner: str) -> datetime:
    receipt_name = att.get("finalizationReceipt")
    index_name = att.get("finalizationIndex")
    require(receipt_name in att["artifacts"] and index_name in att["artifacts"],
            "FinalizationArtifactsMissing")
    receipt = exact_fields(verified_artifact_document(folder, att, receipt_name), {
        "schemaVersion", "sourceSha", "candidateSha", "governanceMode", "decisionId",
        "assurance", "owner", "firstPassRunnerSha256", "finalizedAtUtc", "workflowRunUrl",
        "finalizationIndex", "finalizationIndexSha256", "finalizationIndexBytes",
    }, "FinalizationReceiptFieldsInvalid")
    require(receipt.get("schemaVersion") == 2 and receipt.get("sourceSha") == expected_sha
            and receipt.get("candidateSha") == expected_sha
            and receipt.get("governanceMode") == "single-owner-exception"
            and receipt.get("decisionId") == "OWN-VPS-04"
            and receipt.get("assurance") == ASSURANCE and receipt.get("owner") == owner
            and receipt.get("firstPassRunnerSha256") == first_runner,
            "FinalizationReceiptIdentityMismatch")
    require(receipt.get("finalizationIndex") == index_name
            and receipt.get("finalizationIndexSha256") == att["artifacts"][index_name]["sha256"]
            and receipt.get("finalizationIndexBytes") == att["artifacts"][index_name]["bytes"],
            "FinalizationIndexBindingMismatch")
    require(RUN_URL.fullmatch(str(receipt.get("workflowRunUrl", ""))) is not None,
            "FinalizationWorkflowRunInvalid")
    finalized = utc_timestamp(receipt.get("finalizedAtUtc"), "FinalizationTimestampInvalid")
    require(finalized >= DECISION_EFFECTIVE_UTC and finalized <= now_utc(),
            "FinalizationTimestampOutsideDecision")
    index = exact_fields(verified_artifact_document(folder, att, index_name), {
        "schemaVersion", "sourceSha", "artifacts", "signingStatus",
        "ownerReleaseDecision", "nextStep",
    }, "FinalizationIndexFieldsInvalid")
    require(index.get("schemaVersion") == 1 and index.get("sourceSha") == expected_sha
            and index.get("signingStatus") == "UNSIGNED"
            and index.get("ownerReleaseDecision") == "NOT_ATTACHED",
            "FinalizationIndexInvalid")
    required = {att.get("preflightReceipt"), att.get("linuxReceipt"), att.get("guiReceipt"),
                att.get("routeObservations"), att.get("sqlOracle")}
    ledger = index.get("artifacts")
    require(isinstance(ledger, dict) and None not in required and required <= set(ledger),
            "FinalizationIndexCoverageGap")
    for name in required:
        require(ledger.get(name) == att["artifacts"].get(name),
                "FinalizationIndexArtifactMismatch")
    return finalized


def validate_linux_receipt(folder: Path, att: dict[str, Any], expected_sha: str,
                           owner: str) -> None:
    name = att.get("linuxReceipt")
    require(name in att["artifacts"], "LinuxReceiptArtifactMissing")
    receipt = exact_fields(verified_artifact_document(folder, att, name), {
        "schemaVersion", "sourceSha", "machineSha256", "artifactSha256", "governanceMode",
        "decisionId", "assurance", "owner", "events", "physicalEvidenceReview", "overall",
    }, "LinuxReceiptFieldsInvalid")
    require(receipt.get("schemaVersion") == 2 and receipt.get("sourceSha") == expected_sha
            and receipt.get("machineSha256") == att["environment"]["linuxMachineSha256"]
            and receipt.get("artifactSha256") == att["environment"]["linuxArtifactSha256"]
            and receipt.get("governanceMode") == "single-owner-exception"
            and receipt.get("decisionId") == "OWN-VPS-04" and receipt.get("assurance") == ASSURANCE
            and receipt.get("owner") == owner
            and receipt.get("physicalEvidenceReview") == "OWNER_SECOND_PASS_REQUIRED"
            and receipt.get("overall") == "STRUCTURE_COMPLETE_NOT_REVIEWED",
            "LinuxReceiptIdentityMismatch")
    events = receipt.get("events")
    require(isinstance(events, list) and len(events) == 8, "LinuxReceiptCoverageGap")
    seen: set[str] = set()
    for event in events:
        require(isinstance(event, dict), "LinuxReceiptEventInvalid")
        event_id = event.get("event")
        require(event_id in LINUX_EVENTS and event_id not in seen, "LinuxReceiptCoverageGap")
        seen.add(event_id)
        expected = "EXPECTED_FAILURE_RECOVERED" if event_id == "failed-deploy-rollback" else "PASS"
        require(event.get("outcome") == expected
                and SHA256.fullmatch(str(event.get("evidenceDigest", ""))) is not None
                and SHA256.fullmatch(str(event.get("evidenceFileSha256", ""))) is not None
                and type(event.get("evidenceFileBytes")) is int and event["evidenceFileBytes"] > 0,
                "LinuxReceiptEventInvalid")
        utc_timestamp(event.get("observedAtUtc"), "LinuxReceiptTimestampInvalid")
    require(seen == LINUX_EVENTS, "LinuxReceiptCoverageGap")


def validate_gui_receipt(folder: Path, att: dict[str, Any], expected_sha: str,
                         owner: str, first_runner: str) -> None:
    name = att.get("guiReceipt")
    require(name in att["artifacts"], "GuiReceiptArtifactMissing")
    receipt = exact_fields(verified_artifact_document(folder, att, name), {
        "schemaVersion", "sourceSha", "desktopExecutableSha256", "firstPassRunnerSha256",
        "governanceMode", "decisionId", "assurance", "owner", "checkpoints", "executed",
        "passed", "failed", "skipped", "attendedEvidenceReview", "overall",
    }, "GuiReceiptFieldsInvalid")
    require(receipt.get("schemaVersion") == 2 and receipt.get("sourceSha") == expected_sha
            and receipt.get("desktopExecutableSha256") == att["environment"]["desktopExecutableSha256"]
            and receipt.get("firstPassRunnerSha256") == first_runner
            and receipt.get("governanceMode") == "single-owner-exception"
            and receipt.get("decisionId") == "OWN-VPS-04" and receipt.get("assurance") == ASSURANCE
            and receipt.get("owner") == owner and receipt.get("executed") == 9
            and receipt.get("passed") == 9 and receipt.get("failed") == 0
            and receipt.get("skipped") == 0
            and receipt.get("attendedEvidenceReview") == "OWNER_SECOND_PASS_REQUIRED"
            and receipt.get("overall") == "STRUCTURE_COMPLETE_NOT_REVIEWED",
            "GuiReceiptIdentityMismatch")
    rows = receipt.get("checkpoints")
    require(isinstance(rows, list) and len(rows) == 9, "GuiReceiptCoverageGap")
    seen: set[tuple[str, int]] = set()
    evidence: set[str] = set()
    for row in rows:
        pair = (row.get("id"), row.get("scalePercent"))
        digest = str(row.get("evidenceSha256", ""))
        require(pair in GUI_CHECKPOINTS and pair not in seen and row.get("outcome") == "PASS"
                and SHA256.fullmatch(digest) is not None and digest not in evidence,
                "GuiReceiptCheckpointInvalid")
        utc_timestamp(row.get("observedAtUtc"), "GuiReceiptTimestampInvalid")
        seen.add(pair)
        evidence.add(digest)
    require(seen == GUI_CHECKPOINTS, "GuiReceiptCoverageGap")


def verify_signature(folder: Path, public_key: Path, expected_key_digest: str) -> bytes:
    require(SHA256.fullmatch(expected_key_digest) is not None,
            "TrustedKeyFingerprintMissing")
    require(public_key.is_file() and not public_key.is_symlink(), "TrustedKeyMissing")
    key_bytes = public_key.read_bytes()
    require(sha256(key_bytes) == expected_key_digest, "TrustedKeyFingerprintMismatch")
    payload = folder / "attestation.json"
    signature = folder / "attestation.sig"
    require(payload.is_file() and signature.is_file()
            and not payload.is_symlink() and not signature.is_symlink(),
            "SignedAttestationMissing")
    signed_payload = payload.read_bytes()
    signed_signature = signature.read_bytes()
    require(0 < len(signed_payload) <= MAX_ARTIFACT_BYTES
            and len(signed_signature) == 64, "SignatureSizeInvalid")
    try:
        # OpenSSL must verify the exact bytes that we subsequently parse. Never
        # re-read a caller-controlled pathname after signature verification.
        with TemporaryDirectory(prefix="vps09-verify-") as scratch:
            anchor = Path(scratch)
            key_copy = anchor / "trusted.pub"
            payload_copy = anchor / "attestation.json"
            signature_copy = anchor / "attestation.sig"
            key_copy.write_bytes(key_bytes)
            payload_copy.write_bytes(signed_payload)
            signature_copy.write_bytes(signed_signature)
            key_info = subprocess.run(
                ["openssl", "pkey", "-pubin", "-in", str(key_copy),
                 "-text_pub", "-noout"],
                check=False, capture_output=True, timeout=12)
            require(key_info.returncode == 0 and b"ED25519" in key_info.stdout.upper(),
                    "KeyMustBeEd25519")
            verified = subprocess.run(
                ["openssl", "pkeyutl", "-verify", "-pubin", "-inkey", str(key_copy),
                 "-rawin", "-in", str(payload_copy), "-sigfile", str(signature_copy)],
                check=False, capture_output=True, timeout=12)
            require(verified.returncode == 0, "SignatureInvalid")
    except (OSError, subprocess.TimeoutExpired) as error:
        raise ValueError("VPS09.SignatureVerifierUnavailable") from error
    return signed_payload

def validate_observations(folder: Path, att: dict[str, Any],
                          expected_pairs: set[tuple[str, str]]) -> None:
    location = att.get("routeObservations")
    require(location in att["artifacts"], "RouteObservationArtifactMissing")
    rows = verified_artifact_document(folder, att, location)
    require(rows.get("sourceSha") == att["headSha"] and
            rows.get("executionClass") == "protected-real-sql13",
            "RouteObservationProvenance")
    cases = rows.get("cases")
    require(isinstance(cases, list), "RouteCasesMissing")
    frozen_status = {
        (row["CaseId"], row["Variant"]): row["Status"]
        for row in document(ROOT / document(SPEC_PATH)["frozenObservations"])
    }
    actual: set[tuple[str, str]] = set()
    for row in cases:
        key = (row.get("caseId"), row.get("variant"))
        require(key in expected_pairs and key not in actual, "RouteCaseExtraOrDuplicate")
        actual.add(key)
        require(row.get("status") == "PASS" and row.get("diff") == [],
                "RouteCaseUnverified")
        expected = row.get("expectedPayloadSha256")
        actual_hash = row.get("actualPayloadSha256")
        require(isinstance(expected, str) and SHA256.fullmatch(expected) is not None
                and actual_hash == expected, "RouteCasePayloadMismatch")
        status = row.get("httpStatus")
        require(type(status) is int and 100 <= status <= 599
                and status == row.get("expectedHttpStatus")
                and status == frozen_status[key], "RouteCaseStatusMismatch")
    require(actual == expected_pairs, "RouteCaseCoverageGap")


def validate_oracle(folder: Path, att: dict[str, Any], scenarios: list[str]) -> None:
    location = att.get("sqlOracle")
    require(location in att["artifacts"], "SqlOracleArtifactMissing")
    doc = verified_artifact_document(folder, att, location)
    require(doc.get("sourceSha") == att["headSha"] and
            doc.get("principal") == "separate-read-only-sql" and
            doc.get("executionClass") == "protected-real-sql13",
            "SqlOracleProvenance")
    checks = doc.get("checks")
    require(isinstance(checks, list), "OracleChecksMissing")
    covered: set[str] = set()
    for row in checks:
        scenario = row.get("scenario")
        require(scenario in scenarios and scenario not in covered, "OracleScenarioUnexpected")
        covered.add(scenario)
        expected, actual = row.get("expected"), row.get("actual")
        require(isinstance(expected, dict) and bool(expected)
                and expected == actual and row.get("diff") == [],
                "OracleExpectedActualDiff")
        require(SHA256.fullmatch(str(row.get("querySha256", ""))) is not None,
                "OracleQueryHashMissing")
    require(covered == set(scenarios), "OracleCoverageGap")


def validate_packet(folder: Path, expected_sha: str,
                    trusted_key: Path, trusted_digest: str,
                    expected_first_pass_runner_sha256: str,
                    expected_second_pass_runner_sha256: str) -> None:
    spec, _, pairs = inventory()
    require(SHA40.fullmatch(expected_sha) is not None, "ExpectedShaInvalid")
    raw = verify_signature(folder, trusted_key, trusted_digest)
    att = parse_json(raw.decode("utf-8"))
    require("people" not in att and "operator" not in att and "reviewer" not in att,
            "LegacyGovernanceFieldRefused")
    exact_fields(att, {
        "schemaVersion", "repository", "headSha", "desktopSourceSha",
        "linuxDeployedSourceSha", "environment", "governance", "releaseDecision",
        "routeObservations", "sqlOracle", "preflightReceipt", "linuxReceipt", "guiReceipt",
        "finalizationIndex", "finalizationReceipt", "gates", "ciRuns", "artifacts",
    }, "PacketFieldsInvalid")
    require(att.get("schemaVersion") == 2 and att.get("repository") == spec["repository"],
            "PacketSchemaOrRepository")
    require(att.get("headSha") == expected_sha
            and att.get("desktopSourceSha") == expected_sha
            and att.get("linuxDeployedSourceSha") == expected_sha, "CrossArtifactShaMismatch")
    env = exact_fields(att.get("environment"), {
        "desktop", "host", "sql", "sqlTls", "oracle", "staging", "cleanup",
        "desktopExecutableSha256", "linuxArtifactSha256", "linuxMachineSha256",
        "sqlInstanceSha256",
    }, "EnvironmentFieldsInvalid")
    for name, value in {
        "desktop": "windows-net48-x86-attended",
        "host": "linux-net10-systemd-nginx",
        "sql": "reviewed-isolated-real-sql13",
        "sqlTls": "verified",
        "oracle": "separate-read-only",
        "staging": "protected-single-owner-exception",
        "cleanup": "confirmed",
    }.items():
        require(env.get(name) == value, "EnvironmentNotAdmitted:" + name)
    for name in ("desktopExecutableSha256", "linuxArtifactSha256",
                 "linuxMachineSha256", "sqlInstanceSha256"):
        require(SHA256.fullmatch(str(env.get(name, ""))) is not None,
                "EnvironmentHashMissing:" + name)
    governance = exact_fields(att.get("governance"), {
        "mode", "decisionId", "assurance", "owner", "firstPassRunnerSha256",
        "secondPassRunnerSha256", "ownerPublicKeySha256", "finalizedAtUtc",
    }, "GovernanceFieldsInvalid")
    require(governance.get("mode") == "single-owner-exception"
            and governance.get("decisionId") == "OWN-VPS-04"
            and governance.get("assurance") == ASSURANCE
            and governance.get("owner") == "chung6a8m",
            "SingleOwnerGovernanceMissing")
    owner_key = str(governance.get("ownerPublicKeySha256", ""))
    require(SHA256.fullmatch(owner_key) is not None
            and owner_key == trusted_digest, "OwnerKeyFingerprintMismatch")
    first_runner = str(governance.get("firstPassRunnerSha256", ""))
    second_runner = str(governance.get("secondPassRunnerSha256", ""))
    require(SHA256.fullmatch(first_runner) is not None
            and SHA256.fullmatch(second_runner) is not None
            and first_runner != second_runner, "RunnerSeparationRequired")
    require(SHA256.fullmatch(expected_first_pass_runner_sha256) is not None
            and first_runner == expected_first_pass_runner_sha256,
            "FirstPassRunnerIdentityMismatch")
    require(SHA256.fullmatch(expected_second_pass_runner_sha256) is not None
            and second_runner == expected_second_pass_runner_sha256,
            "SecondPassRunnerIdentityMismatch")
    artifact_names = validate_artifacts(folder, att)
    decision = exact_fields(att.get("releaseDecision"), {
        "status", "owner", "sourceSha", "approvedAtUtc",
    }, "ReleaseDecisionFieldsInvalid")
    require(decision.get("status") == "APPROVED_FOR_PRODUCTION"
            and decision.get("owner") == governance["owner"]
            and decision.get("sourceSha") == expected_sha,
            "ExplicitOwnerReleaseDecisionMissing")
    finalized_at = validate_finalization(folder, att, expected_sha,
                                         governance["owner"], first_runner)
    require(str(governance.get("finalizedAtUtc", "")) ==
            finalized_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
            "GovernanceFinalizationTimestampMismatch")
    approved_at = utc_timestamp(decision.get("approvedAtUtc"),
                                "ReleaseDecisionTimestampInvalid")
    require(approved_at <= now_utc(), "ReleaseDecisionInFuture")
    require(approved_at - finalized_at >= timedelta(hours=24),
            "OwnerCoolingOffIncomplete")
    preflight_location = att.get("preflightReceipt")
    require(preflight_location in artifact_names, "PreflightReceiptArtifactMissing")
    preflight = verified_artifact_document(folder, att, preflight_location)
    exact_fields(preflight, {
        "schemaVersion", "sourceSha", "publicHost", "database", "governanceMode",
        "decisionId", "assurance", "owner", "runnerSha256", "sqlTransport",
    }, "PreflightReceiptFieldsInvalid")
    require(preflight.get("schemaVersion") == 2 and preflight.get("sourceSha") == expected_sha
            and preflight.get("governanceMode") == "single-owner-exception"
            and preflight.get("decisionId") == "OWN-VPS-04"
            and preflight.get("assurance") == ASSURANCE
            and preflight.get("owner") == governance["owner"]
            and preflight.get("runnerSha256") == first_runner,
            "FirstPassRunnerReceiptMismatch")
    validate_linux_receipt(folder, att, expected_sha, governance["owner"])
    validate_gui_receipt(folder, att, expected_sha, governance["owner"], first_runner)
    required = {item["id"] for item in spec["gates"]}
    gates = att.get("gates")
    require(isinstance(gates, dict) and set(gates) == required, "GateSetIncomplete")
    first_evidence: set[str] = set()
    for gate_id, gate in gates.items():
        require(gate.get("status") == "PASS", "GateNotPassed:" + gate_id)
        values = [gate.get(k) for k in ("discovered", "executed", "passed", "failed", "skipped")]
        require(all(type(v) is int and v >= 0 for v in values)
                and values[0] > 0 and values[0] == values[1] == values[2]
                and values[3] == values[4] == 0, "SkippedOrEmptyGate:" + gate_id)
        floor = next(row["minimumExecuted"] for row in spec["gates"]
                     if row["id"] == gate_id)
        require(type(floor) is int and floor > 0 and values[1] >= floor,
                "GateBelowMinimum:" + gate_id)
        evidence = gate.get("evidence")
        require(isinstance(evidence, list) and bool(evidence) and
                all(name in artifact_names for name in evidence),
                "GateEvidenceMissing:" + gate_id)
        require(evidence[0] not in first_evidence, "GatePrimaryEvidenceReused:" + gate_id)
        first_evidence.add(evidence[0])
        require(isinstance(gate.get("runner"), str) and bool(gate["runner"].strip()),
                "GateRunnerMissing:" + gate_id)
    require(att["linuxReceipt"] in gates["VPS09-11"]["evidence"],
            "LinuxReceiptNotBoundToGate")
    require(att["guiReceipt"] in gates["VPS09-12"]["evidence"],
            "GuiReceiptNotBoundToGate")
    ci = att.get("ciRuns")
    require(isinstance(ci, list), "CiRunsMissing")
    ci_names: set[str] = set()
    ci_urls: set[str] = set()
    for entry in ci:
        name = entry.get("workflow")
        require(name in spec["requiredWorkflowFiles"] and name not in ci_names,
                "CiWorkflowExtraOrDuplicate")
        ci_names.add(name)
        url = str(entry.get("url", ""))
        require(entry.get("sourceSha") == expected_sha and
                entry.get("conclusion") == "success" and
                RUN_URL.fullmatch(url) is not None,
                "CiNotVerifiedOnExactHead")
        require(url not in ci_urls, "CiRunUrlReused")
        ci_urls.add(url)
    require(ci_names == set(spec["requiredWorkflowFiles"]), "CiWorkflowMissing")
    finalization = verified_artifact_document(folder, att, att["finalizationReceipt"])
    finalization_ci = next(item for item in ci
                           if item["workflow"] == "vps-09-evidence.yml")
    require(finalization_ci["url"] == finalization["workflowRunUrl"],
            "FinalizationWorkflowNotBoundToCiRun")
    validate_observations(folder, att, pairs)
    validate_oracle(folder, att, spec["oracleScenarios"])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    command = parser.add_subparsers(dest="command", required=True)
    command.add_parser("inventory")
    verify = command.add_parser("verify")
    verify.add_argument("--bundle", type=Path, required=True)
    verify.add_argument("--expected-sha", required=True)
    verify.add_argument("--trusted-key", type=Path, required=True)
    verify.add_argument("--trusted-key-sha256", required=True)
    verify.add_argument("--expected-first-pass-runner-sha256", required=True)
    verify.add_argument("--expected-second-pass-runner-sha256", required=True)
    args = parser.parse_args()
    try:
        spec, routes, observations = inventory()
        if args.command == "inventory":
            print(f"VPS09_SOURCE_INVENTORY_PASS: routes={len(routes)}, "
                  f"frozen_pairs={len(observations)}, gates={len(spec['gates'])}; "
                  "this is NOT live SQL or release evidence")
        else:
            validate_packet(args.bundle, args.expected_sha,
                            args.trusted_key, args.trusted_key_sha256,
                            args.expected_first_pass_runner_sha256,
                            args.expected_second_pass_runner_sha256)
            print("VPS09_SIGNED_PACKET_STRUCTURE_PASS: signature, SHA, ledger, coverage, "
                  "gate counters and Owner assertion; governance=OWN-VPS-04; "
                  "assurance=NO_INDEPENDENT_HUMAN_REVIEW; physical evidence review remains manual")
    except (AttributeError, IndexError, KeyError, TypeError, ValueError, OSError, UnicodeError) as error:
        print(f"VPS09_FAIL_CLOSED: {error}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
