import json
import os
import pathlib
import shutil
import subprocess
import tempfile
import unittest
import uuid


ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "local-simulation" / "Invoke-MyDmsVnLocalSimulation.ps1"
RUNTIME_MODULE = ROOT / "scripts" / "local-simulation" / "LocalSimulation.Runtime.psm1"
STATE_PARENT = ROOT / "TestResults" / "local-simulation"


def powershell():
    return shutil.which("pwsh") or shutil.which("powershell") or "powershell"


@unittest.skipUnless(shutil.which("pwsh") or shutil.which("powershell"), "PowerShell required")
class LocalSimulationOperatorTests(unittest.TestCase):
    def setUp(self):
        self.state = STATE_PARENT / ("test-" + uuid.uuid4().hex)
        self.outside = pathlib.Path(tempfile.mkdtemp(prefix="mydmsvn-unrelated-"))
        (self.outside / "keep.txt").write_text("unrelated", encoding="utf-8")

    def tearDown(self):
        if self.state.exists() and not self.state.is_symlink():
            shutil.rmtree(self.state, ignore_errors=True)
        elif self.state.is_symlink():
            self.state.unlink(missing_ok=True)
        shutil.rmtree(self.outside, ignore_errors=True)

    def run_operator(self, action, *extra):
        return subprocess.run(
            [
                powershell(),
                "-NoLogo",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(SCRIPT),
                "-Action",
                action,
                "-StateRoot",
                str(self.state),
                "-OutputFormat",
                "Json",
                *extra,
            ],
            cwd=ROOT,
            text=True,
            capture_output=True,
        )

    def write_receipt(self, **changes):
        self.state.mkdir(parents=True, exist_ok=True)
        receipt = {
            "schemaVersion": 1,
            "instanceId": "11111111-2222-4333-8444-555555555555",
            "sourceSha": "a" * 40,
            "stateRoot": str(self.state.resolve()),
            "createdAtUtc": "2026-11-19T00:00:00Z",
            "phase": "ready",
            "docker": {
                "container": "mydmsvn-local-sim-sql",
                "volume": "mydmsvn-local-sim-sql-data",
                "created": False,
            },
            "wsl": {
                "distribution": "Ubuntu",
                "service": "mydmsvn-local-sim.service",
                "installed": False,
            },
        }
        receipt.update(changes)
        (self.state / "receipt.json").write_text(json.dumps(receipt), encoding="utf-8")
        return receipt

    def test_preflight_emits_diagnostics_without_creating_state(self):
        result = self.run_operator("Preflight")

        self.assertIn(result.returncode, (0, 2), result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("Preflight", report["action"])
        self.assertIn(report["status"], ("READY", "BLOCKED"))
        self.assertGreaterEqual(len(report["checks"]), 6)
        self.assertIn("DockerResourceNamesAvailable", {item["name"] for item in report["checks"]})
        self.assertFalse(self.state.exists())

    def test_initialize_dry_run_reports_exact_resources_without_mutation(self):
        result = self.run_operator("Initialize", "-DryRun")

        self.assertEqual(0, result.returncode, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("PLANNED", report["status"])
        self.assertEqual("mydmsvn-local-sim-sql", report["resources"]["container"])
        self.assertEqual("mydmsvn-local-sim.service", report["resources"]["service"])
        self.assertEqual("https://localhost:50443", report["resources"]["origin"])
        self.assertFalse(self.state.exists())

    def test_lifecycle_actions_require_receipt(self):
        for action in ("Start", "Verify", "Stop"):
            with self.subTest(action=action):
                result = self.run_operator(action)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual(
                    "LocalSimulation.ReceiptMissing", json.loads(result.stdout)["code"]
                )

    def test_start_refuses_partial_initialization_receipt(self):
        self.write_receipt(phase="initializing")

        result = self.run_operator("Start")

        self.assertNotEqual(0, result.returncode)
        self.assertEqual("LocalSimulation.ReceiptPhaseInvalid", json.loads(result.stdout)["code"])

    @unittest.skipUnless(os.name == "nt", "Windows-to-WSL conversion is Windows-specific")
    def test_runtime_module_converts_windows_path_without_truncation(self):
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($p) Convert-ToWslPath $p }} '{ROOT}'"
        )

        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(result.stdout.strip().startswith("/mnt/c/"), result.stdout)

    def test_runtime_initializer_accepts_an_empty_state_directory(self):
        self.state.mkdir(parents=True)
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"try {{ Initialize-MyDmsVnLocalSimulation -RepoRoot '{self.outside}' "
            f"-StateRoot '{self.state}' }} catch {{ $_.Exception.Message }}"
        )
        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("LocalSimulation.GitStateUnavailable", result.stdout)
        self.assertNotIn("LocalSimulation.StateAlreadyExists", result.stdout)

    def test_reset_refuses_missing_receipt_and_preserves_unrelated_file(self):
        result = self.run_operator(
            "Reset", "-ConfirmInstanceId", "11111111-2222-4333-8444-555555555555"
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual("LocalSimulation.ReceiptMissing", json.loads(result.stdout)["code"])
        self.assertEqual("unrelated", (self.outside / "keep.txt").read_text(encoding="utf-8"))

    def test_reset_refuses_forged_state_root(self):
        receipt = self.write_receipt(stateRoot=str(self.outside.resolve()))
        result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

        self.assertNotEqual(0, result.returncode)
        self.assertEqual("LocalSimulation.ReceiptStateMismatch", json.loads(result.stdout)["code"])
        self.assertTrue(self.state.exists())
        self.assertTrue((self.outside / "keep.txt").exists())

    def test_reset_reports_malformed_receipt_without_leaking_input(self):
        self.state.mkdir(parents=True)
        secret = "NeverPrintThisSqlPassword!42"
        (self.state / "receipt.json").write_text(
            json.dumps({"unexpected": secret}), encoding="utf-8"
        )

        result = self.run_operator(
            "Reset", "-ConfirmInstanceId", "11111111-2222-4333-8444-555555555555"
        )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual("LocalSimulation.ReceiptInvalid", json.loads(result.stdout)["code"])
        self.assertNotIn(secret, result.stdout + result.stderr)
        self.assertTrue(self.state.exists())

    def test_reset_deletes_only_receipt_owned_state(self):
        receipt = self.write_receipt()

        result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("RESET", json.loads(result.stdout)["status"])
        self.assertFalse(self.state.exists())
        self.assertEqual("unrelated", (self.outside / "keep.txt").read_text(encoding="utf-8"))

    @unittest.skipUnless(os.name == "nt", "junction behavior is Windows-specific")
    def test_reset_refuses_reparse_point_state_root(self):
        target = STATE_PARENT / ("target-" + uuid.uuid4().hex)
        target.mkdir(parents=True)
        try:
            made = subprocess.run(
                ["cmd", "/c", "mklink", "/J", str(self.state), str(target)],
                capture_output=True,
                text=True,
            )
            if made.returncode != 0:
                self.skipTest("Windows junction creation is unavailable")
            receipt = self.write_receipt()

            result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

            self.assertNotEqual(0, result.returncode)
            self.assertEqual("LocalSimulation.ReparsePointRefused", json.loads(result.stdout)["code"])
            self.assertTrue(target.exists())
        finally:
            if self.state.exists():
                subprocess.run(["cmd", "/c", "rmdir", str(self.state)], capture_output=True)
            shutil.rmtree(target, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
