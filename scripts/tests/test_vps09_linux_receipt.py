import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from scripts.vps09_linux_receipt import ReceiptBlocked, finalize, initialize, record


SHA = "0123456789abcdef0123456789abcdef01234567"
DIGEST = "a" * 64
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


class Vps09LinuxReceiptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="vps09-linux-receipt-")
        self.root = Path(self.temp.name)
        self.state = self.root / "state.json"
        initialize(self.state, SHA, DIGEST, "b" * 64, "chung6a8m")

    def tearDown(self):
        self.temp.cleanup()

    def test_complete_event_set_produces_structural_not_reviewed_receipt(self):
        for event in EVENTS:
            self._record(event, "EXPECTED_FAILURE_RECOVERED" if event == "failed-deploy-rollback" else "PASS")

        output = self.root / "receipt.json"
        receipt = finalize(self.state, output)

        self.assertEqual(8, len(receipt["events"]))
        self.assertEqual("STRUCTURE_COMPLETE_NOT_REVIEWED", receipt["overall"])
        self.assertEqual("OWNER_SECOND_PASS_REQUIRED", receipt["physicalEvidenceReview"])
        self.assertEqual("OWN-VPS-04", receipt["decisionId"])
        self.assertEqual("chung6a8m", receipt["owner"])
        self.assertNotIn(str(self.root), output.read_text(encoding="utf-8"))

    def test_missing_event_is_refused(self):
        for event in EVENTS[:-1]:
            self._record(event, "EXPECTED_FAILURE_RECOVERED" if event == "failed-deploy-rollback" else "PASS")

        with self.assertRaisesRegex(ReceiptBlocked, "EventCoverageGap"):
            finalize(self.state, self.root / "receipt.json")

    def test_failed_deploy_cannot_be_relabelled_pass_after_recovery(self):
        with self.assertRaisesRegex(ReceiptBlocked, "FailedDeployOutcomeInvalid"):
            self._record("failed-deploy-rollback", "PASS")

    def test_stale_source_event_is_refused(self):
        event_file = self._event_file("nginx-proxy", "PASS")
        document = json.loads(event_file.read_text(encoding="utf-8"))
        document["sourceSha"] = "f" * 40
        event_file.write_text(json.dumps(document), encoding="utf-8")

        with self.assertRaisesRegex(ReceiptBlocked, "EventSourceMismatch"):
            record(self.state, event_file)

    def test_duplicate_event_is_refused(self):
        self._record("nginx-proxy", "PASS")

        with self.assertRaisesRegex(ReceiptBlocked, "EventDuplicate"):
            self._record("nginx-proxy", "PASS")

    def _record(self, event, outcome):
        return record(self.state, self._event_file(event, outcome))

    def _event_file(self, event, outcome):
        path = self.root / (event + ".json")
        body = {
            "schemaVersion": 2,
            "event": event,
            "sourceSha": SHA,
            "machineSha256": DIGEST,
            "governanceMode": "single-owner-exception",
            "decisionId": "OWN-VPS-04",
            "owner": "chung6a8m",
            "observedAtUtc": "2026-11-18T12:00:00Z",
            "outcome": outcome,
            "evidenceDigest": hashlib.sha256(event.encode()).hexdigest(),
        }
        path.write_text(json.dumps(body), encoding="utf-8")
        return path


if __name__ == "__main__":
    unittest.main()
