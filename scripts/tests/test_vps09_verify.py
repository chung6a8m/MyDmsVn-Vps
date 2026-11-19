#!/usr/bin/env python3
"""Synthetic negative fixtures for VPS-09 receipt admission, NOT live acceptance."""
import copy
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from datetime import datetime, timezone
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
        self.clock = patch.object(verifier, "now_utc", return_value=datetime(
            2026, 11, 21, tzinfo=timezone.utc))
        self.clock.start()
        self.addCleanup(self.clock.stop)
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
        self.preflight = {
            "schemaVersion": 2,
            "sourceSha": SHA,
            "publicHost": "staging.invalid",
            "database": "isolated_vps09",
            "governanceMode": "single-owner-exception",
            "decisionId": "OWN-VPS-04",
            "assurance": "NO_INDEPENDENT_HUMAN_REVIEW",
            "owner": "chung6a8m",
            "runnerSha256": HEX,
            "sqlTransport": "encrypt-config-admitted; handshake-required-by-oracles",
        }
        self.linux_receipt = {
            "schemaVersion": 2, "sourceSha": SHA, "machineSha256": HEX,
            "artifactSha256": HEX, "governanceMode": "single-owner-exception",
            "decisionId": "OWN-VPS-04", "assurance": "NO_INDEPENDENT_HUMAN_REVIEW",
            "owner": "chung6a8m", "physicalEvidenceReview": "OWNER_SECOND_PASS_REQUIRED",
            "overall": "STRUCTURE_COMPLETE_NOT_REVIEWED",
            "events": [{
                "event": event, "observedAtUtc": "2026-11-19T00:00:00Z",
                "outcome": "EXPECTED_FAILURE_RECOVERED" if event == "failed-deploy-rollback" else "PASS",
                "evidenceDigest": format(index + 20, "064x"),
                "evidenceFileSha256": format(index + 40, "064x"), "evidenceFileBytes": 100 + index,
            } for index, event in enumerate(sorted(verifier.LINUX_EVENTS))],
        }
        self.gui_receipt = {
            "schemaVersion": 2, "sourceSha": SHA, "desktopExecutableSha256": HEX,
            "firstPassRunnerSha256": HEX, "governanceMode": "single-owner-exception",
            "decisionId": "OWN-VPS-04", "assurance": "NO_INDEPENDENT_HUMAN_REVIEW",
            "owner": "chung6a8m", "executed": 9, "passed": 9, "failed": 0, "skipped": 0,
            "attendedEvidenceReview": "OWNER_SECOND_PASS_REQUIRED",
            "overall": "STRUCTURE_COMPLETE_NOT_REVIEWED",
            "checkpoints": [{
                "id": checkpoint[0], "scalePercent": checkpoint[1], "outcome": "PASS",
                "observedAtUtc": "2026-11-19T00:00:00Z",
                "evidenceSha256": format(index + 1, "064x"),
            } for index, checkpoint in enumerate(sorted(verifier.GUI_CHECKPOINTS))],
        }
        self.attestation = {
            "schemaVersion": 2,
            "repository": "chung6a8m/MyDmsVn-Vps",
            "headSha": SHA,
            "desktopSourceSha": SHA,
            "linuxDeployedSourceSha": SHA,
            "environment": {
                "desktop": "windows-net48-x86-attended",
                "host": "linux-net10-systemd-nginx",
                "sql": "reviewed-isolated-real-sql13",
                "sqlTls": "verified",
                "oracle": "separate-read-only",
                "staging": "protected-single-owner-exception",
                "cleanup": "confirmed",
                "desktopExecutableSha256": HEX,
                "linuxArtifactSha256": HEX,
                "linuxMachineSha256": HEX,
                "sqlInstanceSha256": HEX,
            },
            "governance": {
                "mode": "single-owner-exception",
                "decisionId": "OWN-VPS-04",
                "assurance": "NO_INDEPENDENT_HUMAN_REVIEW",
                "owner": "chung6a8m",
                "firstPassRunnerSha256": HEX,
                "secondPassRunnerSha256": "c" * 64,
                "ownerPublicKeySha256": self.pub_hash,
                "finalizedAtUtc": "2026-11-19T00:00:00Z",
            },
            "releaseDecision": {
                "status": "APPROVED_FOR_PRODUCTION", "owner": "chung6a8m",
                "sourceSha": SHA,
                "approvedAtUtc": "2026-11-20T00:00:00Z",
            },
            "routeObservations": "route-observations.json",
            "sqlOracle": "sql-oracle.json",
            "preflightReceipt": "preflight-receipt.json",
            "linuxReceipt": "linux-receipt.json",
            "guiReceipt": "gui-receipt.json",
            "finalizationIndex": "finalization-index.json",
            "finalizationReceipt": "finalization-receipt.json",
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
        write_json(self.packet / "preflight-receipt.json", self.preflight)
        write_json(self.packet / "linux-receipt.json", self.linux_receipt)
        write_json(self.packet / "gui-receipt.json", self.gui_receipt)
        (self.packet / "other-evidence.txt").write_text(
            "SYNTHETIC FIXTURE: NOT LIVE ACCEPTANCE\n", encoding="utf-8")
        names = ["route-observations.json", "sql-oracle.json", "preflight-receipt.json",
                 "linux-receipt.json", "gui-receipt.json", "other-evidence.txt"]
        for row in self.spec["gates"]:
            name = "gates/" + row["id"] + ".json"
            write_json(self.packet / name, {"gate": row["id"], "synthetic": True})
            names.append(name)
        self.attestation["gates"]["VPS09-11"]["evidence"][0] = "linux-receipt.json"
        self.attestation["gates"]["VPS09-12"]["evidence"][0] = "gui-receipt.json"
        self.attestation["artifacts"] = {}
        for name in names:
            blob = (self.packet / name).read_bytes()
            self.attestation["artifacts"][name] = {
                "bytes": len(blob), "sha256": hashlib.sha256(blob).hexdigest(),
            }
        core_names = ["route-observations.json", "sql-oracle.json", "preflight-receipt.json",
                      "linux-receipt.json", "gui-receipt.json"]
        finalization_index = {
            "schemaVersion": 1, "sourceSha": SHA,
            "artifacts": {name: self.attestation["artifacts"][name] for name in core_names},
            "signingStatus": "UNSIGNED", "ownerReleaseDecision": "NOT_ATTACHED",
            "nextStep": "synthetic test only",
        }
        write_json(self.packet / "finalization-index.json", finalization_index)
        index_blob = (self.packet / "finalization-index.json").read_bytes()
        finalization_receipt = {
            "schemaVersion": 2, "sourceSha": SHA, "candidateSha": SHA,
            "governanceMode": "single-owner-exception", "decisionId": "OWN-VPS-04",
            "assurance": "NO_INDEPENDENT_HUMAN_REVIEW", "owner": "chung6a8m",
            "firstPassRunnerSha256": HEX, "finalizedAtUtc": "2026-11-19T00:00:00Z",
            "workflowRunUrl": next(row["url"] for row in self.attestation["ciRuns"]
                                   if row["workflow"] == "vps-09-evidence.yml"),
            "finalizationIndex": "finalization-index.json",
            "finalizationIndexSha256": hashlib.sha256(index_blob).hexdigest(),
            "finalizationIndexBytes": len(index_blob),
        }
        write_json(self.packet / "finalization-receipt.json", finalization_receipt)
        for name in ("finalization-index.json", "finalization-receipt.json"):
            blob = (self.packet / name).read_bytes()
            self.attestation["artifacts"][name] = {
                "bytes": len(blob), "sha256": hashlib.sha256(blob).hexdigest(),
            }

    def sign_raw(self, raw):
        (self.packet / "attestation.json").write_text(raw, encoding="utf-8")
        subprocess.run(["openssl", "pkeyutl", "-sign", "-inkey", str(self.key),
                        "-rawin", "-in", str(self.packet / "attestation.json"),
                        "-out", str(self.packet / "attestation.sig")],
                       check=True, capture_output=True)

    def refresh_finalization_binding(self):
        index_path = self.packet / "finalization-index.json"
        index = json.loads(index_path.read_text(encoding="utf-8"))
        for name in list(index["artifacts"]):
            index["artifacts"][name] = self.attestation["artifacts"][name]
        write_json(index_path, index)
        index_blob = index_path.read_bytes()
        self.attestation["artifacts"]["finalization-index.json"] = {
            "bytes": len(index_blob), "sha256": hashlib.sha256(index_blob).hexdigest(),
        }
        receipt_path = self.packet / "finalization-receipt.json"
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        receipt["finalizationIndexSha256"] = hashlib.sha256(index_blob).hexdigest()
        receipt["finalizationIndexBytes"] = len(index_blob)
        write_json(receipt_path, receipt)
        receipt_blob = receipt_path.read_bytes()
        self.attestation["artifacts"]["finalization-receipt.json"] = {
            "bytes": len(receipt_blob), "sha256": hashlib.sha256(receipt_blob).hexdigest(),
        }

    def sign(self):
        self.sign_raw(json.dumps(self.attestation, indent=2) + "\n")

    def verify(self):
        verifier.validate_packet(self.packet, SHA, self.public, self.pub_hash, HEX, "c" * 64)

    def rejects(self, match):
        self.sign()
        with self.assertRaisesRegex(ValueError, match):
            self.verify()

    def test_valid_synthetic_packet_only_validates_structure(self):
        self.sign()
        self.verify()

    def test_duplicate_member_in_signed_attestation_refused(self):
        raw = json.dumps(self.attestation, indent=2) + "\n"
        raw = raw.replace('  "headSha": ',
                          '  "headSha": "' + SHA + '",\n  "headSha": ', 1)
        self.sign_raw(raw)
        with self.assertRaisesRegex(ValueError, "DuplicateJsonMember:headSha"):
            self.verify()

    def test_duplicate_member_in_hashed_oracle_refused(self):
        raw = json.dumps(self.oracle, indent=2) + "\n"
        raw = raw.replace('  "sourceSha": ',
                          '  "sourceSha": "' + SHA + '",\n  "sourceSha": ', 1)
        target = self.packet / "sql-oracle.json"
        target.write_text(raw, encoding="utf-8")
        blob = target.read_bytes()
        self.attestation["artifacts"]["sql-oracle.json"] = {
            "bytes": len(blob), "sha256": hashlib.sha256(blob).hexdigest(),
        }
        self.refresh_finalization_binding()
        self.rejects("DuplicateJsonMember:sourceSha")

    def test_attestation_swap_after_openssl_verification_cannot_change_parsed_bytes(self):
        self.sign()
        target = self.packet / "attestation.json"
        original = target.read_bytes()
        altered = copy.deepcopy(self.attestation)
        altered["releaseDecision"]["status"] = "DENIED"
        real_run = subprocess.run

        def swap_after_verify(args, *rest, **kwargs):
            result = real_run(args, *rest, **kwargs)
            if "-verify" in args and result.returncode == 0:
                target.write_text(json.dumps(altered) + "\n", encoding="utf-8")
            return result

        with patch.object(verifier.subprocess, "run", side_effect=swap_after_verify):
            signed = verifier.verify_signature(self.packet, self.public, self.pub_hash)
        self.assertEqual(original, signed)

    def test_hashed_oracle_swap_after_ledger_check_refused(self):
        self.sign()
        target = self.packet / "sql-oracle.json"
        original_check = verifier.validate_artifacts

        def swap_after_ledger(folder, attestation):
            listed = original_check(folder, attestation)
            altered = copy.deepcopy(self.oracle)
            altered["checks"][0]["expected"]["value"] = "99.9999"
            altered["checks"][0]["actual"]["value"] = "99.9999"
            target.write_text(json.dumps(altered) + "\n", encoding="utf-8")
            return listed

        with patch.object(verifier, "validate_artifacts", side_effect=swap_after_ledger):
            with self.assertRaisesRegex(ValueError, "ArtifactChangedAfterLedgerVerification"):
                self.verify()

    def test_wrong_trusted_public_key_fingerprint(self):
        self.sign()
        with self.assertRaisesRegex(ValueError, "TrustedKeyFingerprintMismatch"):
            verifier.validate_packet(self.packet, SHA, self.public, "c" * 64, HEX, "c" * 64)

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

    def test_unapproved_governance_mode_refused(self):
        self.attestation["governance"]["mode"] = "self-approved"
        self.rejects("SingleOwnerGovernanceMissing")

    def test_legacy_people_governance_is_refused_in_schema_v2(self):
        self.attestation["people"] = {
            "operator": "operator01", "reviewer": "reviewer02", "owner": "chung6a8m",
        }
        self.rejects("LegacyGovernanceFieldRefused")

    def test_wrong_owner_decision_refused(self):
        self.attestation["governance"]["decisionId"] = "OWN-VPS-03"
        self.rejects("SingleOwnerGovernanceMissing")

    def test_owner_key_must_match_trusted_key_refused(self):
        self.attestation["governance"]["ownerPublicKeySha256"] = "d" * 64
        self.rejects("OwnerKeyFingerprintMismatch")

    def test_first_and_second_pass_runner_must_differ(self):
        self.attestation["governance"]["secondPassRunnerSha256"] = HEX
        self.rejects("RunnerSeparationRequired")

    def test_first_pass_runner_must_match_preflight_receipt(self):
        self.preflight["runnerSha256"] = "d" * 64
        self.refresh_evidence()
        self.rejects("FirstPassRunnerReceiptMismatch")

    def test_first_pass_runner_must_match_external_environment_pin(self):
        self.sign()
        with self.assertRaisesRegex(ValueError, "FirstPassRunnerIdentityMismatch"):
            verifier.validate_packet(self.packet, SHA, self.public, self.pub_hash,
                                     "d" * 64, "c" * 64)

    def test_second_pass_runner_must_match_verifying_machine(self):
        self.sign()
        with self.assertRaisesRegex(ValueError, "SecondPassRunnerIdentityMismatch"):
            verifier.validate_packet(self.packet, SHA, self.public, self.pub_hash, HEX, "d" * 64)

    def test_cooling_off_shorter_than_24_hours_refused(self):
        self.attestation["releaseDecision"]["approvedAtUtc"] = "2026-11-19T23:59:59Z"
        self.rejects("OwnerCoolingOffIncomplete")

    def test_governance_timestamp_must_match_protected_finalization_receipt(self):
        self.attestation["governance"]["finalizedAtUtc"] = "2026-11-19T00:00:01Z"
        self.rejects("GovernanceFinalizationTimestampMismatch")

    def test_finalization_before_decision_is_refused(self):
        receipt_path = self.packet / "finalization-receipt.json"
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        receipt["finalizedAtUtc"] = "2026-11-18T00:00:00Z"
        write_json(receipt_path, receipt)
        blob = receipt_path.read_bytes()
        self.attestation["artifacts"]["finalization-receipt.json"] = {
            "bytes": len(blob), "sha256": hashlib.sha256(blob).hexdigest(),
        }
        self.rejects("FinalizationTimestampOutsideDecision")

    def test_generic_linux_gate_marker_cannot_replace_typed_receipt(self):
        self.attestation["linuxReceipt"] = "gates/VPS09-11.json"
        self.attestation["gates"]["VPS09-11"]["evidence"] = ["gates/VPS09-11.json"]
        self.rejects("FinalizationIndexCoverageGap|LinuxReceiptFieldsInvalid")

    def test_gui_receipt_must_match_first_runner(self):
        self.gui_receipt["firstPassRunnerSha256"] = "d" * 64
        self.refresh_evidence()
        self.rejects("GuiReceiptIdentityMismatch")

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

    def test_invalid_owner_decision_timestamp_refused(self):
        self.attestation["releaseDecision"]["approvedAtUtc"] = "2026-99-99T00:00:00Z"
        self.rejects("ReleaseDecisionTimestampInvalid")

    def test_future_owner_decision_timestamp_refused(self):
        self.attestation["releaseDecision"]["approvedAtUtc"] = "2099-01-01T00:00:00Z"
        self.rejects("ReleaseDecisionInFuture")


if __name__ == "__main__":
    unittest.main()
