#!/usr/bin/env python3
"""Synthetic negative fixtures for VPS-09 receipt admission, NOT live acceptance."""
import copy
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import vps09_verify as verifier

SHA = "a" * 40
HEX = "b" * 64


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


class Vps09EvidenceGateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="vps09-negative-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.packet = self.root / "packet"
        self.packet.mkdir()
        self.key = self.root / "signing.key"
        self.public = self.root / "signing.pub"
        subprocess.run(["openssl", "genpkey", "-algorithm", "ED25519", "-out", str(self.key)],
                       check=True, capture_output=True)
        subprocess.run(["openssl", "pkey", "-in", str(self.key), "-pubout",
                        "-out", str(self.public)], check=True, capture_output=True)
        self.pub_hash = hashlib.sha256(self.public.read_bytes()).hexdigest()
        spec, _, pairs = verifier.inventory()
        self.spec = spec
        frozen_status = {
            (row["CaseId"], row["Variant"]): row["Status"]
            for row in verifier.document(verifier.ROOT / spec["frozenObservations"])
        }
        self.observations = {
            "sourceSha": SHA,
            "executionClass": "protected-real-sql13",
            "cases": [{
                "caseId": case,
                "variant": variant,
                "status": "PASS",
                "httpStatus": frozen_status[(case, variant)],
                "expectedHttpStatus": frozen_status[(case, variant)],
                "expectedPayloadSha256": HEX,
                "actualPayloadSha256": HEX,
                "diff": [],
            } for case, variant in sorted(pairs)]
        }
        self.oracle = {
            "sourceSha": SHA,
            "executionClass": "protected-real-sql13",
            "principal": "separate-read-only-sql",
            "checks": [{
                "scenario": scenario,
                "expected": {"rows": 1, "value": "12.3456"},
                "actual": {"rows": 1, "value": "12.3456"},
                "diff": [],
                "querySha256": HEX,
            } for scenario in spec["oracleScenarios"]]
        }
        self.attestation = {
            "schemaVersion": 1,
            "repository": "chung6a8m/MyDmsVn-Vps",
            "headSha": SHA,
            "desktopSourceSha": SHA,
            "linuxDeployedSourceSha": SHA,
            "environment": {
                "desktop": "windows-net48-x86-attended",
                "host": "linux-net10-systemd-nginx",
                "sql": "reviewed-isolated-real-sql13",
                "sqlTls": "verified",
                "oracle": "independent-read-only",
                "staging": "protected-independent-review",
                "cleanup": "confirmed",
                "desktopExecutableSha256": HEX,
                "linuxArtifactSha256": HEX,
                "linuxMachineSha256": HEX,
                "sqlInstanceSha256": HEX,
            },
            "people": {"operator": "operator01", "reviewer": "reviewer02",
                       "owner": "owner03"},
            "releaseDecision": {
                "status": "APPROVED_FOR_PRODUCTION", "owner": "owner03",
                "date": "2026-11-18",
            },
            "routeObservations": "route-observations.json",
            "sqlOracle": "sql-oracle.json",
            "gates": {row["id"]: {
                "status": "PASS", "discovered": row["minimumExecuted"],
                "executed": row["minimumExecuted"], "passed": row["minimumExecuted"],
                "failed": 0, "skipped": 0, "runner": "synthetic-negative-test",
                "evidence": ["gates/" + row["id"] + ".json"],
            } for row in spec["gates"]},
            "ciRuns": [{
                "workflow": name, "sourceSha": SHA, "conclusion": "success",
                "url": "https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/"
                       + str(123456 + index),
            } for index, name in enumerate(spec["requiredWorkflowFiles"])],
            "artifacts": {},
        }
        self.refresh_evidence()

    def refresh_evidence(self):
        write_json(self.packet / "route-observations.json", self.observations)
        write_json(self.packet / "sql-oracle.json", self.oracle)
        (self.packet / "other-evidence.txt").write_text(
            "SYNTHETIC FIXTURE: NOT LIVE ACCEPTANCE\n", encoding="utf-8")
        names = ["route-observations.json", "sql-oracle.json", "other-evidence.txt"]
        for row in self.spec["gates"]:
            name = "gates/" + row["id"] + ".json"
            write_json(self.packet / name, {"gate": row["id"], "synthetic": True})
            names.append(name)
        for name in names:
            blob = (self.packet / name).read_bytes()
            self.attestation["artifacts"][name] = {
                "bytes": len(blob), "sha256": hashlib.sha256(blob).hexdigest(),
            }

    def sign(self):
        write_json(self.packet / "attestation.json", self.attestation)
        subprocess.run(["openssl", "pkeyutl", "-sign", "-inkey", str(self.key),
                        "-rawin", "-in", str(self.packet / "attestation.json"),
                        "-out", str(self.packet / "attestation.sig")],
                       check=True, capture_output=True)

    def verify(self):
        verifier.validate_packet(self.packet, SHA, self.public, self.pub_hash)

    def rejects(self, match):
        self.sign()
        with self.assertRaisesRegex(ValueError, match):
            self.verify()

    def test_valid_synthetic_packet_only_validates_structure(self):
        self.sign()
        self.verify()

    def test_wrong_trusted_public_key_fingerprint(self):
        self.sign()
        with self.assertRaisesRegex(ValueError, "TrustedKeyFingerprintMismatch"):
            verifier.validate_packet(self.packet, SHA, self.public, "c" * 64)

    def test_signature_tamper_refused(self):
        self.sign()
        self.attestation["headSha"] = "c" * 40
        write_json(self.packet / "attestation.json", self.attestation)
        with self.assertRaisesRegex(ValueError, "SignatureInvalid"):
            self.verify()

    def test_missing_exact_head_refused(self):
        self.attestation["linuxDeployedSourceSha"] = "c" * 40
        self.rejects("CrossArtifactShaMismatch")

    def test_skipped_required_gate_refused(self):
        self.attestation["gates"]["VPS09-12"]["skipped"] = 1
        self.rejects("SkippedOrEmptyGate")

    def test_partial_or_missing_gate_refused(self):
        del self.attestation["gates"]["VPS09-11"]
        self.rejects("GateSetIncomplete")

    def test_missing_live_sql_case_refused(self):
        self.observations["cases"].pop()
        self.refresh_evidence()
        self.rejects("RouteCaseCoverageGap")

    def test_duplicate_live_sql_case_refused(self):
        self.observations["cases"].append(copy.deepcopy(self.observations["cases"][0]))
        self.refresh_evidence()
        self.rejects("RouteCaseExtraOrDuplicate")

    def test_source_only_route_observations_refused(self):
        self.observations["executionClass"] = "sql-free-differential"
        self.refresh_evidence()
        self.rejects("RouteObservationProvenance")

    def test_sql_oracle_expected_actual_diff_refused(self):
        self.oracle["checks"][0]["actual"] = {"rows": 2}
        self.refresh_evidence()
        self.rejects("OracleExpectedActualDiff")

    def test_missing_oracle_scenario_refused(self):
        self.oracle["checks"].pop()
        self.refresh_evidence()
        self.rejects("OracleCoverageGap")

    def test_non_independent_reviewer_refused(self):
        self.attestation["people"]["reviewer"] = "operator01"
        self.rejects("ReviewMustBeIndependent")

    def test_owner_release_not_approved_refused(self):
        self.attestation["releaseDecision"]["status"] = "SOURCE_ONLY"
        self.rejects("ExplicitOwnerReleaseDecisionMissing")

    def test_ci_stale_head_refused(self):
        self.attestation["ciRuns"][0]["sourceSha"] = "c" * 40
        self.rejects("CiNotVerifiedOnExactHead")

    def test_evidence_modification_refused(self):
        self.sign()
        (self.packet / "other-evidence.txt").write_text("altered")
        with self.assertRaisesRegex(ValueError, "ArtifactSizeMismatch|ArtifactDigestMismatch"):
            self.verify()

    def test_evidence_symlink_refused(self):
        target = self.packet / "other-evidence.txt"
        target.unlink()
        target.symlink_to(self.public)
        self.rejects("SymlinkArtifactRefused")

    def test_unlisted_file_refused(self):
        (self.packet / "unlisted.txt").write_text("unlisted")
        self.rejects("UnlistedOrMissingArtifact")

    def test_path_traversal_refused(self):
        self.attestation["artifacts"]["../outside.txt"] = {"bytes": 1, "sha256": HEX}
        self.rejects("UnsafeArtifactPath")

    def test_live_receipt_cannot_claim_disposable_ci_as_staging(self):
        self.attestation["environment"]["staging"] = "ephemeral-container"
        self.rejects("EnvironmentNotAdmitted")

    def test_short_live_sql_293_gate_refused(self):
        gate = self.attestation["gates"]["VPS09-02"]
        gate["discovered"] = gate["executed"] = gate["passed"] = 292
        self.rejects("GateBelowMinimum:VPS09-02")

    def test_short_attended_9_checkpoint_gate_refused(self):
        gate = self.attestation["gates"]["VPS09-12"]
        gate["discovered"] = gate["executed"] = gate["passed"] = 8
        self.rejects("GateBelowMinimum:VPS09-12")

    def test_short_vps08_8_gate_refused(self):
        gate = self.attestation["gates"]["VPS09-11"]
        gate["discovered"] = gate["executed"] = gate["passed"] = 7
        self.rejects("GateBelowMinimum:VPS09-11")

    def test_missing_required_ci_workflow_refused(self):
        self.attestation["ciRuns"].pop()
        self.rejects("CiWorkflowMissing")

    def test_duplicate_ci_run_url_refused(self):
        self.attestation["ciRuns"][0]["url"] = self.attestation["ciRuns"][1]["url"]
        self.rejects("CiRunUrlReused")

    def test_reused_gate_primary_evidence_refused(self):
        self.attestation["gates"]["VPS09-12"]["evidence"] = [
            self.attestation["gates"]["VPS09-11"]["evidence"][0]
        ]
        self.rejects("GatePrimaryEvidenceReused")

    def test_wrong_http_status_against_frozen_contract_refused(self):
        case = self.observations["cases"][0]
        alternative = 400 if case["httpStatus"] != 400 else 200
        case["httpStatus"] = alternative
        case["expectedHttpStatus"] = alternative
        self.refresh_evidence()
        self.rejects("RouteCaseStatusMismatch")

    def test_casefold_operator_reviewer_identity_refused(self):
        self.attestation["people"]["reviewer"] = "OPERATOR01"
        self.rejects("ReviewMustBeIndependent")

    def test_invalid_owner_decision_date_refused(self):
        self.attestation["releaseDecision"]["date"] = "2026-99-99"
        self.rejects("ReleaseDecisionDateInvalid")

    def test_future_owner_decision_date_refused(self):
        self.attestation["releaseDecision"]["date"] = "2099-01-01"
        self.rejects("ReleaseDecisionInFuture")


if __name__ == "__main__":
    unittest.main()
