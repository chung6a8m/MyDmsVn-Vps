import json
import tempfile
import unittest
from pathlib import Path

from scripts.vps09_gui_checklist import ChecklistBlocked, CHECKPOINTS, validate


SHA = "0123456789abcdef0123456789abcdef01234567"


class Vps09GuiChecklistTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="vps09-gui-")
        self.path = Path(self.temp.name) / "gui.json"

    def tearDown(self):
        self.temp.cleanup()

    def test_nine_attended_checkpoints_produce_structural_receipt(self):
        self._write(self._document())

        receipt = validate(self.path)

        self.assertEqual(9, receipt["executed"])
        self.assertEqual(9, receipt["passed"])
        self.assertEqual("STRUCTURE_COMPLETE_NOT_REVIEWED", receipt["overall"])
        self.assertEqual("OWNER_SECOND_PASS_REQUIRED", receipt["attendedEvidenceReview"])
        self.assertEqual("NO_INDEPENDENT_HUMAN_REVIEW", receipt["assurance"])
        self.assertEqual(9, len(receipt["checkpoints"]))

    def test_missing_checkpoint_is_refused(self):
        document = self._document()
        document["checkpoints"].pop()
        self._write(document)

        with self.assertRaisesRegex(ChecklistBlocked, "CheckpointCoverageGap"):
            validate(self.path)

    def test_legacy_independent_reviewer_schema_is_refused(self):
        document = self._document()
        document["schemaVersion"] = 1
        document["operator"] = "operator01"
        document["reviewer"] = "reviewer02"
        self._write(document)

        with self.assertRaisesRegex(ChecklistBlocked, "ChecklistFieldsInvalid"):
            validate(self.path)

    def test_reused_checkpoint_evidence_is_refused(self):
        document = self._document()
        document["checkpoints"][1]["evidenceSha256"] = document["checkpoints"][0]["evidenceSha256"]
        self._write(document)

        with self.assertRaisesRegex(ChecklistBlocked, "CheckpointEvidenceReused"):
            validate(self.path)

    def _document(self):
        return {
            "schemaVersion": 2,
            "sourceSha": SHA,
            "desktopExecutableSha256": "f" * 64,
            "firstPassRunnerSha256": "e" * 64,
            "governanceMode": "single-owner-exception",
            "decisionId": "OWN-VPS-04",
            "assurance": "NO_INDEPENDENT_HUMAN_REVIEW",
            "owner": "chung6a8m",
            "sessionType": "interactive",
            "checkpoints": [
                {
                    "id": checkpoint[0],
                    "scalePercent": checkpoint[1],
                    "outcome": "PASS",
                    "observedAtUtc": "2026-11-18T12:00:00Z",
                    "evidenceSha256": format(index + 1, "064x"),
                }
                for index, checkpoint in enumerate(CHECKPOINTS)
            ],
        }

    def _write(self, document):
        self.path.write_text(json.dumps(document), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
