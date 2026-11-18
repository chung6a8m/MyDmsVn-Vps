import copy
import json
import tempfile
import unittest
from pathlib import Path

from scripts.vps09_manifest import ManifestError, validate_manifest


ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "docs" / "testing" / "vps-09-live-scenarios.json"


class Vps09LiveManifestTests(unittest.TestCase):
    def test_repository_manifest_covers_every_frozen_route_variant(self):
        stats = validate_manifest(ROOT, MANIFEST)

        self.assertEqual(
            {"routes": 57, "scenarios": 293, "oracleScenarios": 12}, stats
        )

    def test_scenario_without_executable_request_is_refused(self):
        document = json.loads(MANIFEST.read_text(encoding="utf-8"))
        document["scenarios"][0].pop("request")

        with self.assertRaisesRegex(ManifestError, "ScenarioRequestMissing"):
            self._validate(document)

    def test_duplicate_pair_is_refused(self):
        document = json.loads(MANIFEST.read_text(encoding="utf-8"))
        document["scenarios"][1] = copy.deepcopy(document["scenarios"][0])

        with self.assertRaisesRegex(ManifestError, "ScenarioPairDuplicate"):
            self._validate(document)

    def test_secret_shaped_manifest_content_is_refused(self):
        document = json.loads(MANIFEST.read_text(encoding="utf-8"))
        document["scenarios"][0]["request"]["password"] = "not-allowed"

        with self.assertRaisesRegex(ManifestError, "SecretMaterialForbidden"):
            self._validate(document)

    def test_unknown_oracle_mapping_is_refused(self):
        document = json.loads(MANIFEST.read_text(encoding="utf-8"))
        document["scenarios"][0]["oracleScenario"] = "not-registered"

        with self.assertRaisesRegex(ManifestError, "OracleScenarioUnknown"):
            self._validate(document)

    def test_missing_protected_seed_data_policy_is_refused(self):
        document = json.loads(MANIFEST.read_text(encoding="utf-8"))
        document.pop("seedDataPolicy")

        with self.assertRaisesRegex(ManifestError, "SeedDataPolicyInvalid"):
            self._validate(document)

    def _validate(self, document):
        with tempfile.TemporaryDirectory(prefix="vps09-manifest-") as folder:
            path = Path(folder) / "manifest.json"
            path.write_text(json.dumps(document), encoding="utf-8")
            return validate_manifest(ROOT, path)


if __name__ == "__main__":
    unittest.main()
