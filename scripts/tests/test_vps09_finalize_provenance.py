import hashlib
import json
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

from scripts.vps09_finalize_provenance import REPO, verify_provenance


SHA = "a" * 40
JOB = "Validate attended GUI, Linux receipt and cleanup (no release approval)"


class Vps09FinalizeProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.TemporaryDirectory()
        self.addCleanup(self.work.cleanup)
        self.packet = Path(self.work.name)
        self.receipt = {
            "workflowRunUrl": f"https://github.com/{REPO}/actions/runs/1234",
            "finalizedAtUtc": "2026-11-19T00:03:00Z",
        }
        self.attestation = {
            "schemaVersion": 2, "headSha": SHA,
            "finalizationReceipt": "finalization-receipt.json",
            "releaseDecision": {"approvedAtUtc": "2026-11-20T00:06:00Z"},
            "artifacts": {},
        }
        self.run = {
            "id": 1234,
            "path": ".github/workflows/vps-09-evidence.yml",
            "head_sha": SHA, "head_branch": "main",
            "event": "workflow_dispatch",
            "actor": {"login": "chung6a8m"},
            "triggering_actor": {"login": "chung6a8m"},
            "status": "completed", "conclusion": "success",
        }
        self.jobs = {"total_count": 1, "jobs": [{
            "name": JOB, "status": "completed", "conclusion": "success",
            "started_at": "2026-11-19T00:00:00Z",
            "completed_at": "2026-11-19T00:05:00Z",
        }]}
        self.now = datetime(2026, 11, 21, tzinfo=timezone.utc)

    def request(self, path, token):
        self.assertEqual("fake-token", token)
        if path == f"/repos/{REPO}/actions/runs/1234":
            return self.run
        if path == f"/repos/{REPO}/actions/runs/1234/jobs?per_page=100":
            return self.jobs
        raise AssertionError("Unknown path: " + path)

    def check(self):
        blob = json.dumps(self.receipt).encode("utf-8")
        (self.packet / "finalization-receipt.json").write_bytes(blob)
        self.attestation["artifacts"]["finalization-receipt.json"] = {
            "sha256": hashlib.sha256(blob).hexdigest(), "bytes": len(blob),
        }
        (self.packet / "attestation.json").write_text(
            json.dumps(self.attestation), encoding="utf-8")
        verify_provenance(self.packet, SHA, "fake-token",
                          fetch=self.request, now=self.now)

    def test_real_finalization_and_elapsed_cooling_admitted(self):
        self.check()

    def test_matching_source_only_run_without_protected_finalize_job_refused(self):
        self.jobs["jobs"][0]["name"] = "Source inventory"
        with self.assertRaisesRegex(ValueError, "ProtectedFinalizeJobMissingOrFailed"):
            self.check()

    def test_failed_protected_finalize_refused_even_if_workflow_green(self):
        self.jobs["jobs"][0]["conclusion"] = "skipped"
        with self.assertRaisesRegex(ValueError, "ProtectedFinalizeJobMissingOrFailed"):
            self.check()

    def test_owner_actor_mismatch_refused(self):
        self.run["actor"]["login"] = "another-user"
        with self.assertRaisesRegex(ValueError, "ProtectedFinalizeActorMismatch"):
            self.check()

    def test_wrong_finalization_candidate_refused(self):
        self.run["head_sha"] = "b" * 40
        with self.assertRaisesRegex(ValueError, "ProtectedFinalizeRunIdentityMismatch"):
            self.check()

    def test_backdated_receipt_outside_gitHub_job_refused(self):
        self.receipt["finalizedAtUtc"] = "2026-11-18T23:59:59Z"
        with self.assertRaisesRegex(ValueError, "FinalizationTimestampOutsideProtectedJob"):
            self.check()

    def test_approval_must_be_24h_after_actual_job_completion(self):
        self.attestation["releaseDecision"]["approvedAtUtc"] = "2026-11-20T00:04:59Z"
        with self.assertRaisesRegex(ValueError, "ApprovalPrecedesProtectedCoolingOff"):
            self.check()

    def test_github_clock_enforces_cooling_even_with_later_signed_approval(self):
        self.now = datetime(2026, 11, 20, 0, 4, 59, tzinfo=timezone.utc)
        self.attestation["releaseDecision"]["approvedAtUtc"] = "2026-11-20T00:04:50Z"
        with self.assertRaisesRegex(ValueError, "ApprovalPrecedesProtectedCoolingOff"):
            self.check()

    def test_signer_cannot_swap_receipt_without_updating_ledger(self):
        blob = json.dumps(self.receipt).encode("utf-8")
        (self.packet / "finalization-receipt.json").write_bytes(blob)
        self.attestation["artifacts"]["finalization-receipt.json"] = {
            "sha256": "0" * 64, "bytes": len(blob),
        }
        (self.packet / "attestation.json").write_text(
            json.dumps(self.attestation), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "FinalizationReceiptDigestMismatch"):
            verify_provenance(self.packet, SHA, "fake-token",
                              fetch=self.request, now=self.now)


if __name__ == "__main__":
    unittest.main()
