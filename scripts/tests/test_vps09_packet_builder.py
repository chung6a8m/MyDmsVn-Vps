import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from scripts.vps09_packet_builder import PacketBlocked, build_index


SHA = "0123456789abcdef0123456789abcdef01234567"


class Vps09PacketBuilderTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="vps09-packet-builder-")
        self.root = Path(self.temp.name)
        (self.root / "route-observations.json").write_text("[]\n", encoding="utf-8")
        (self.root / "sql-oracle.json").write_text("[]\n", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def test_builder_hashes_exact_files_without_creating_release_approval(self):
        index = build_index(self.root, SHA,
                            ["route-observations.json", "sql-oracle.json"])

        self.assertEqual("UNSIGNED", index["signingStatus"])
        self.assertEqual("NOT_ATTACHED", index["ownerReleaseDecision"])
        self.assertNotIn("releaseDecision", index)
        self.assertEqual(
            hashlib.sha256((self.root / "route-observations.json").read_bytes()).hexdigest(),
            index["artifacts"]["route-observations.json"]["sha256"],
        )

    def test_parent_traversal_is_refused(self):
        with self.assertRaisesRegex(PacketBlocked, "ArtifactPathInvalid"):
            build_index(self.root, SHA, ["../outside.json"])

    def test_duplicate_artifact_is_refused(self):
        with self.assertRaisesRegex(PacketBlocked, "ArtifactDuplicate"):
            build_index(self.root, SHA,
                        ["route-observations.json", "route-observations.json"])


if __name__ == "__main__":
    unittest.main()
