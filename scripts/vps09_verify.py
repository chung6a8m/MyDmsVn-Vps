#!/usr/bin/env python3
"""VPS-09: fail-closed independent signed *evidence* verifier, not an E2E runner.

A valid signature binds the named signer's assertions to file digests, not the
truth of physical SQL/VPS/GUI operations. External review remains mandatory.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
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


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError("VPS09." + message)


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
    rows = document(folder / location)
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
    doc = document(folder / location)
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
                    trusted_key: Path, trusted_digest: str) -> None:
    spec, _, pairs = inventory()
    require(SHA40.fullmatch(expected_sha) is not None, "ExpectedShaInvalid")
    raw = verify_signature(folder, trusted_key, trusted_digest)
    att = parse_json(raw.decode("utf-8"))
    require(att.get("schemaVersion") == 1 and att.get("repository") == spec["repository"],
            "PacketSchemaOrRepository")
    require(att.get("headSha") == expected_sha
            and att.get("desktopSourceSha") == expected_sha
            and att.get("linuxDeployedSourceSha") == expected_sha, "CrossArtifactShaMismatch")
    env = att.get("environment")
    require(isinstance(env, dict), "EnvironmentMissing")
    for name, value in {
        "desktop": "windows-net48-x86-attended",
        "host": "linux-net10-systemd-nginx",
        "sql": "reviewed-isolated-real-sql13",
        "sqlTls": "verified",
        "oracle": "independent-read-only",
        "staging": "protected-independent-review",
        "cleanup": "confirmed",
    }.items():
        require(env.get(name) == value, "EnvironmentNotAdmitted:" + name)
    for name in ("desktopExecutableSha256", "linuxArtifactSha256",
                 "linuxMachineSha256", "sqlInstanceSha256"):
        require(SHA256.fullmatch(str(env.get(name, ""))) is not None,
                "EnvironmentHashMissing:" + name)
    people = att.get("people", {})
    require(all(isinstance(people.get(k), str) and len(people[k].strip()) >= 3
                for k in ("operator", "reviewer", "owner")), "PeopleMissing")
    require(people["operator"].casefold() != people["reviewer"].casefold() and
            people["operator"].casefold() != people["owner"].casefold(),
            "ReviewMustBeIndependent")
    decision = att.get("releaseDecision", {})
    require(decision.get("status") == "APPROVED_FOR_PRODUCTION"
            and decision.get("owner") == people["owner"]
            and re.fullmatch(r"20[0-9]{2}-[0-9]{2}-[0-9]{2}", str(decision.get("date", ""))),
            "ExplicitOwnerReleaseDecisionMissing")
    from datetime import date
    try:
        decision_date = date.fromisoformat(decision["date"])
    except ValueError as error:
        raise ValueError("VPS09.ReleaseDecisionDateInvalid") from error
    require(decision_date <= date.today(), "ReleaseDecisionInFuture")
    artifact_names = validate_artifacts(folder, att)
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
    args = parser.parse_args()
    try:
        spec, routes, observations = inventory()
        if args.command == "inventory":
            print(f"VPS09_SOURCE_INVENTORY_PASS: routes={len(routes)}, "
                  f"frozen_pairs={len(observations)}, gates={len(spec['gates'])}; "
                  "this is NOT live SQL or release evidence")
        else:
            validate_packet(args.bundle, args.expected_sha,
                            args.trusted_key, args.trusted_key_sha256)
            print("VPS09_SIGNED_PACKET_STRUCTURE_PASS: signature, SHA, ledger, coverage, "
                  "gate counters and Owner assertion; physical evidence still requires "
                  "independent human review")
    except (AttributeError, IndexError, KeyError, TypeError, ValueError, OSError, UnicodeError) as error:
        print(f"VPS09_FAIL_CLOSED: {error}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
