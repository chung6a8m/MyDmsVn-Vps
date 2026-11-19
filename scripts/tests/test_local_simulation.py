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


def docker_engine_id():
    result = subprocess.run(
        ["docker", "info", "--format", "{{.ID}}"], text=True, capture_output=True
    )
    if result.returncode != 0:
        raise unittest.SkipTest("reachable local Docker engine required")
    return result.stdout.strip()


@unittest.skipUnless(shutil.which("pwsh") or shutil.which("powershell"), "PowerShell required")
class LocalSimulationOperatorTests(unittest.TestCase):
    def setUp(self):
        self.state = STATE_PARENT / ("test-" + uuid.uuid4().hex)
        self.outside = pathlib.Path(tempfile.mkdtemp(prefix="mydmsvn-unrelated-"))
        self.local_app_data = self.outside / "localappdata"
        self.binding = self.local_app_data / "MyDmsVn" / "LocalSimulation" / "binding.json"
        self.process_env = os.environ.copy()
        self.process_env["LOCALAPPDATA"] = str(self.local_app_data)
        (self.outside / "keep.txt").write_text("unrelated", encoding="utf-8")

    def tearDown(self):
        if self.state.exists() and not self.state.is_symlink():
            shutil.rmtree(self.state, ignore_errors=True)
        elif self.state.is_symlink():
            self.state.unlink(missing_ok=True)
        shutil.rmtree(self.outside, ignore_errors=True)
        pathlib.Path(str(self.state.resolve()) + ".binding.json").unlink(missing_ok=True)

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
            env=self.process_env,
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
                "engineId": docker_engine_id(),
                "intent": False,
                "created": False,
            },
            "wsl": {
                "distribution": "Ubuntu",
                "service": "mydmsvn-local-sim.service",
                "installed": False,
            },
            "artifacts": {
                "bashSha256": None,
                "sqlHelperSha256": None,
                "hostSha256": None,
            },
        }
        receipt.update(changes)
        (self.state / "receipt.json").write_text(json.dumps(receipt), encoding="utf-8")
        return receipt

    def write_binding(self, receipt):
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($s) $p=Join-Path $s 'receipt.json'; "
            " $r=Get-Content $p -Raw|ConvertFrom-Json; New-LocalSimulationRegistration $s $r $p } "
            f"'{self.state}'"
        )
        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

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

    def test_reset_dry_run_is_refused_before_any_mutation(self):
        result = self.run_operator("Reset", "-DryRun")

        self.assertEqual(2, result.returncode)
        self.assertEqual("LocalSimulation.DryRunUnsupported", json.loads(result.stdout)["code"])
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
        self.assertEqual("LocalSimulation.RegistrationMissing", json.loads(result.stdout)["code"])

    @unittest.skipUnless(os.name == "nt", "Windows-to-WSL conversion is Windows-specific")
    def test_runtime_module_converts_windows_path_without_truncation(self):
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($p) Convert-ToWslPath $p }} '{ROOT}'"
        )

        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(result.stdout.strip().startswith("/mnt/c/"), result.stdout)

    def test_runtime_ca_import_is_noninteractive_and_identity_checked(self):
        source = RUNTIME_MODULE.read_text(encoding="utf-8")
        self.assertIn("'-user', '-f', '-addstore', 'Root'", source)
        self.assertIn("LocalSimulation.CertificateImportMismatch", source)
        self.assertNotIn("Import-Certificate", source)

    @unittest.skipUnless(os.name == "nt", "native stderr behavior is Windows-specific")
    def test_native_capture_accepts_successful_stderr_on_windows_powershell(self):
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            "& (Get-Module LocalSimulation.Runtime) { "
            "$r=Invoke-NativeCapture 'cmd.exe' @('/d','/c','echo informational 1>&2'); "
            "if($r.ExitCode -ne 0){throw 'unexpected exit'}; 'NATIVE_CAPTURE_PASS' }"
        )
        result = subprocess.run(
            ["powershell.exe", "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("NATIVE_CAPTURE_PASS", result.stdout)

    def test_runtime_binds_registration_checkout_and_executed_artifacts(self):
        source = RUNTIME_MODULE.read_text(encoding="utf-8")
        for token in (
            "cleanupComplete",
            "LocalSimulation.RegistrationMismatch",
            "LocalSimulation.SourceIdentityMismatch",
            "LocalSimulation.ArtifactManifestMismatch",
            "LocalSimulation.ArtifactIdentityMismatch",
        ):
            self.assertIn(token, source)
        self.assertNotIn("{{ index", source)
        self.assertNotIn("{{range .Mounts}}", source)

    def test_artifact_manifest_detects_dependent_assembly_drift(self):
        artifact = self.outside / "artifact"
        artifact.mkdir()
        (artifact / "entry.dll").write_text("entry", encoding="utf-8")
        (artifact / "dependency.dll").write_text("dependency", encoding="utf-8")
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($p) "
            "$expected=New-ArtifactManifest $p; "
            "Set-Content -LiteralPath (Join-Path $p 'dependency.dll') -Value 'tampered'; "
            "try { Assert-ArtifactManifest $p $expected; 'DRIFT_NOT_DETECTED' } "
            "catch { $_.Exception.Message } } "
            f"'{artifact}'"
        )

        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("LocalSimulation.ArtifactIdentityMismatch", result.stdout)
        self.assertNotIn("DRIFT_NOT_DETECTED", result.stdout)

    def test_artifact_manifest_rejects_unexpected_configuration_file(self):
        artifact = self.outside / "artifact-extra"
        artifact.mkdir()
        (artifact / "entry.dll").write_text("entry", encoding="utf-8")
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($p) "
            "$expected=New-ArtifactManifest $p; "
            "Set-Content -LiteralPath (Join-Path $p 'appsettings.Production.json') -Value '{}'; "
            "try { Assert-ArtifactManifest $p $expected; 'EXTRA_NOT_DETECTED' } "
            "catch { $_.Exception.Message } } "
            f"'{artifact}'"
        )

        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("LocalSimulation.ArtifactIdentityMismatch", result.stdout)
        self.assertNotIn("EXTRA_NOT_DETECTED", result.stdout)

    @unittest.skipUnless(os.name == "nt", "Windows ACL behavior is Windows-specific")
    def test_registration_refuses_acl_granted_to_another_principal(self):
        receipt = self.write_receipt()
        self.write_binding(receipt)
        changed = subprocess.run(
            ["icacls.exe", str(self.binding), "/grant", "*S-1-1-0:R"],
            env=self.process_env,
            text=True,
            capture_output=True,
        )
        self.assertEqual(0, changed.returncode, changed.stdout + changed.stderr)

        result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

        self.assertEqual(2, result.returncode)
        self.assertEqual("LocalSimulation.RegistrationAclInvalid", json.loads(result.stdout)["code"])
        self.assertTrue(self.state.exists())

    def test_start_waits_for_sql_verification_before_starting_wsl_service(self):
        source = RUNTIME_MODULE.read_text(encoding="utf-8")
        start = source.split("function Start-MyDmsVnLocalSimulation", 1)[1].split(
            "function Verify-MyDmsVnLocalSimulation", 1
        )[0]
        self.assertIn("Wait-LocalSimulationSqlReady $StateRoot $Receipt", start)
        self.assertLess(
            start.index("Wait-LocalSimulationSqlReady"),
            start.index("Invoke-WslHelper $RepoRoot 'start'"),
        )

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
            env=self.process_env,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("LocalSimulation.GitStateUnavailable", result.stdout)
        self.assertNotIn("LocalSimulation.StateAlreadyExists", result.stdout)

    @unittest.skipUnless(os.name == "nt", "Windows ACL behavior is Windows-specific")
    def test_runtime_acl_hardening_keeps_owned_files_readable(self):
        protected = self.outside / "protected"
        protected.mkdir()
        sample = protected / "sample.txt"
        sample.write_text("owned-readable", encoding="utf-8")
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($p) "
            f"Protect-LocalDirectory $p; Get-Content -LiteralPath (Join-Path $p 'sample.txt') -Raw }} "
            f"'{protected}'"
        )

        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("owned-readable", result.stdout.strip())

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
        self.assertEqual("LocalSimulation.RegistrationMissing", json.loads(result.stdout)["code"])
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
        self.assertEqual("LocalSimulation.RegistrationMissing", json.loads(result.stdout)["code"])
        self.assertNotIn(secret, result.stdout + result.stderr)
        self.assertTrue(self.state.exists())

    def test_reset_deletes_only_receipt_owned_state(self):
        receipt = self.write_receipt()
        self.write_binding(receipt)

        result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("RESET", json.loads(result.stdout)["status"])
        self.assertFalse(self.state.exists())
        self.assertEqual("unrelated", (self.outside / "keep.txt").read_text(encoding="utf-8"))

    def test_reset_resumes_local_cleanup_after_receipt_was_partially_deleted(self):
        receipt = self.write_receipt()
        self.write_binding(receipt)
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"$r=Get-Content '{self.state / 'receipt.json'}' -Raw|ConvertFrom-Json; "
            f"Complete-LocalSimulationRegistration -StateRoot '{self.state}' -Receipt $r"
        )
        completed = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )
        self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
        (self.state / "receipt.json").unlink()

        result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertFalse(self.state.exists())
        self.assertFalse(self.binding.exists())

    def test_reset_recovers_when_authoritative_journal_precedes_receipt_mirror(self):
        receipt = self.write_receipt()
        self.write_binding(receipt)
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($s) "
            "$p=Join-Path $s 'receipt.json'; $r=Get-Content $p -Raw|ConvertFrom-Json; "
            "$r.phase='journal-ahead'; Update-LocalSimulationRegistration $s $r $p } "
            f"'{self.state}'"
        )
        updated = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )
        self.assertEqual(0, updated.returncode, updated.stdout + updated.stderr)

        result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertFalse(self.state.exists())
        self.assertFalse(self.binding.exists())

    def test_reset_refuses_replacement_directory_even_with_copied_receipt(self):
        receipt = self.write_receipt()
        original_receipt = (self.state / "receipt.json").read_bytes()
        self.write_binding(receipt)
        displaced = STATE_PARENT / ("displaced-" + uuid.uuid4().hex)
        self.state.rename(displaced)
        self.state.mkdir(parents=True)
        (self.state / "receipt.json").write_bytes(original_receipt)
        try:
            result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

            self.assertEqual(2, result.returncode)
            self.assertEqual("LocalSimulation.RegistrationMismatch", json.loads(result.stdout)["code"])
            self.assertTrue(self.state.exists())
            self.assertTrue(displaced.exists())
        finally:
            shutil.rmtree(displaced, ignore_errors=True)

    def test_reset_refuses_fabricated_false_acquisition_receipt_without_binding(self):
        receipt = self.write_receipt()
        (self.state / "keep.txt").write_text("must-remain", encoding="utf-8")

        result = self.run_operator("Reset", "-ConfirmInstanceId", receipt["instanceId"])

        self.assertEqual(2, result.returncode)
        self.assertEqual("LocalSimulation.RegistrationMissing", json.loads(result.stdout)["code"])
        self.assertEqual("must-remain", (self.state / "keep.txt").read_text(encoding="utf-8"))

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

    @unittest.skipUnless(os.name == "nt", "junction behavior is Windows-specific")
    def test_reset_refuses_reparse_point_ancestor(self):
        junction = STATE_PARENT / ("junction-" + uuid.uuid4().hex)
        target = pathlib.Path(tempfile.mkdtemp(prefix="mydmsvn-junction-target-"))
        original_state = self.state
        try:
            made = subprocess.run(
                ["cmd", "/c", "mklink", "/J", str(junction), str(target)],
                capture_output=True,
                text=True,
            )
            if made.returncode != 0:
                self.skipTest("Windows junction creation is unavailable")
            self.state = junction / "child"

            result = self.run_operator("Reset", "-ConfirmInstanceId", "11111111-2222-4333-8444-555555555555")

            self.assertEqual(2, result.returncode)
            self.assertEqual("LocalSimulation.ReparsePointRefused", json.loads(result.stdout)["code"])
            self.assertTrue(target.exists())
        finally:
            self.state = original_state
            if junction.exists():
                subprocess.run(["cmd", "/c", "rmdir", str(junction)], capture_output=True)
            shutil.rmtree(target, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
