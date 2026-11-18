import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "Invoke-Vps09IntegratedAcceptance.ps1"
SHA = "0123456789abcdef0123456789abcdef01234567"


@unittest.skipUnless(shutil.which("pwsh"), "PowerShell 7 is required")
class Vps09WindowsIntegratedScriptTests(unittest.TestCase):
    def test_script_has_no_powershell_parser_errors(self):
        command = (
            "$tokens=$null;$errors=$null;"
            f"[void][Management.Automation.Language.Parser]::ParseFile('{SCRIPT}',[ref]$tokens,[ref]$errors);"
            "if($errors.Count){$errors|% Message;exit 1}"
        )

        result = subprocess.run(["pwsh", "-NoProfile", "-Command", command],
                                capture_output=True, text=True)

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_prepare_refuses_missing_admission_before_creating_evidence(self):
        with tempfile.TemporaryDirectory(prefix="vps09-windows-negative-") as folder:
            evidence = Path(folder) / "evidence"
            env = os.environ.copy()
            env.pop("VPS09_ISOLATED_SQL_ADMISSION", None)
            result = subprocess.run([
                "pwsh", "-NoProfile", "-File", str(SCRIPT),
                "-Phase", "Prepare", "-ReviewedSha", SHA,
                "-Origin", "https://staging.invalid", "-EvidenceDirectory", str(evidence),
                "-Operator", "operator01", "-Reviewer", "reviewer02", "-Owner", "reviewer02",
                "-RoutePlanPath", "missing-routes.json", "-OraclePlanPath", "missing-oracles.json",
            ], capture_output=True, text=True, env=env)

            self.assertNotEqual(0, result.returncode)
            self.assertIn("VPS09.IsolatedSqlApprovalMissing", result.stdout + result.stderr)
            self.assertFalse(evidence.exists())


if __name__ == "__main__":
    unittest.main()
