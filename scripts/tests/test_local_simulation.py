import hashlib
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
    override = os.environ.get("MYDMSVN_TEST_DOCKER_ENGINE_ID")
    if override:
        return override
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
        docker_override = self.process_env.get("MYDMSVN_TEST_DOCKER_ENGINE_ID")
        if docker_override and os.name == "nt":
            fake_bin = self.outside / "fake-bin"
            fake_bin.mkdir()
            (fake_bin / "docker.cmd").write_text(
                "@echo off\r\n"
                f'if "%1"=="info" (echo {docker_override}& exit /b 0)\r\n'
                "exit /b 1\r\n",
                encoding="utf-8",
            )
            (fake_bin / "wsl.cmd").write_text(
                "@echo off\r\n"
                "setlocal EnableDelayedExpansion\r\n"
                'if not "%4"=="wslpath" exit /b 1\r\n'
                'set "p=%~6"\r\n'
                'set "drive=!p:~0,1!"\r\n'
                'for %%L in (a b c d e f g h i j k l m n o p q r s t u v w x y z) do if /I "!drive!"=="%%L" set "drive=%%L"\r\n'
                'set "rest=!p:~3!"\r\n'
                'set "rest=!rest:\\=/!"\r\n'
                "echo /mnt/!drive!/!rest!\r\n"
                "exit /b 0\r\n",
                encoding="utf-8",
            )
            self.process_env["PATH"] = str(fake_bin) + os.pathsep + self.process_env["PATH"]
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
        diagnostic = ""
        if result.returncode != 0:
            binding_literal = str(self.binding).replace("'", "''")
            diagnostic_command = (
                "$current=[Security.Principal.WindowsIdentity]::GetCurrent(); "
                f"$paths=@((Split-Path -Parent '{binding_literal}'),'{binding_literal}'); "
                "$items=foreach($p in $paths){if(Test-Path -LiteralPath $p){$a=Get-Acl -LiteralPath $p; "
                "[pscustomobject]@{pathType=$(if((Get-Item -LiteralPath $p).PSIsContainer){'directory'}else{'file'}); "
                "currentName=$current.Name;currentSid=$current.User.Value;owner=$a.Owner;protected=$a.AreAccessRulesProtected; "
                "rules=@($a.Access|ForEach-Object{$_.IdentityReference.Value+':'+$_.AccessControlType+':'+$_.FileSystemRights})}}}; "
                "$items|ConvertTo-Json -Depth 5 -Compress"
            )
            observed = subprocess.run(
                [powershell(), "-NoLogo", "-NoProfile", "-Command", diagnostic_command],
                cwd=ROOT,
                env=self.process_env,
                text=True,
                capture_output=True,
            )
            diagnostic = "\nACL diagnostic: " + observed.stdout + observed.stderr
        self.assertEqual(0, result.returncode, result.stdout + result.stderr + diagnostic)

    def test_preflight_emits_diagnostics_without_creating_state(self):
        result = self.run_operator("Preflight")

        self.assertIn(result.returncode, (0, 2), result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("Preflight", report["action"])
        self.assertIn(report["status"], ("READY", "BLOCKED"))
        self.assertGreaterEqual(len(report["checks"]), 6)
        self.assertIn("DockerResourceNamesAvailable", {item["name"] for item in report["checks"]})
        self.assertFalse(self.state.exists())

    @unittest.skipUnless(os.name == "nt", "Windows Docker preflight")
    def test_preflight_blocks_when_docker_reports_less_than_two_gibibytes(self):
        fake_bin = self.outside / "memory-probe"
        fake_bin.mkdir()
        self.process_env["PATH"] = str(fake_bin) + os.pathsep + self.process_env["PATH"]
        for memory, expected in (("1008324608", False), ("2147483648", True), ("unavailable", False)):
            with self.subTest(memory=memory):
                (fake_bin / "docker.cmd").write_text(
                    f"@echo off\nif \"%1\"==\"info\" (echo {memory}& exit /b 0)\nexit /b 1\n",
                    encoding="utf-8",
                )
                report = json.loads(self.run_operator("Preflight").stdout)
                by_name = {check["name"]: check["passed"] for check in report["checks"]}
                self.assertIn("DockerSqlMemory", by_name)
                self.assertEqual(expected, by_name["DockerSqlMemory"])
                self.assertFalse(self.state.exists())

    def candidate_fixture(self, source="b" * 40, extra_path=None):
        candidate = self.outside / "candidate"
        candidate.mkdir()
        digests = []
        for kind, filenames in (
            ("desktop", ["MyDmsVn.Desktop.App.exe"]),
            ("host", [
                "MyDmsVn.Server.AspNetCoreHost.dll",
                "MyDmsVn.Server.AspNetCoreHost.deps.json",
                "MyDmsVn.Server.AspNetCoreHost.runtimeconfig.json",
            ]),
        ):
            directory = candidate / kind
            directory.mkdir()
            files = {}
            for name in filenames:
                payload = name.encode("ascii")
                (directory / name).write_bytes(payload)
                files[name] = {"size": len(payload), "sha256": hashlib.sha256(payload).hexdigest()}
            if kind == "desktop":
                manifest = directory / "vps03-desktop-manifest.json"
                document = {
                    "sourceSha": source, "targetFramework": "net48", "platformTarget": "x86",
                    "files": [{"path": name, "length": row["size"], "sha256": row["sha256"]}
                              for name, row in files.items()],
                }
            else:
                manifest = directory / "vps08-manifest.json"
                if extra_path is not None:
                    files[extra_path] = next(iter(files.values()))
                document = {"source_sha": source, "format": "vps08-linux-v1", "files": files}
            manifest.write_bytes(json.dumps(document).encode("ascii"))
            digests.append(hashlib.sha256(manifest.read_bytes()).hexdigest())
        return candidate, digests

    def run_candidate_dry_run(self, candidate, digests, source="b" * 40):
        return self.run_operator(
            "Initialize", "-DryRun", "-CandidateRoot", str(candidate),
            "-CandidateSourceSha", source, "-DesktopManifestSha256", digests[0],
            "-HostManifestSha256", digests[1],
        )

    def test_initialize_dry_run_refuses_explicitly_empty_candidate_parameters(self):
        result = self.run_operator(
            "Initialize", "-DryRun", "-CandidateRoot", "",
            "-CandidateSourceSha", "", "-DesktopManifestSha256", "", "-HostManifestSha256", "",
        )
        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        self.assertEqual("LocalSimulation.CandidateArgumentsInvalid", json.loads(result.stdout)["code"])
        self.assertFalse(self.state.exists())

    def test_initialize_dry_run_reports_manifest_bound_prebuilt_pair(self):
        candidate, digests = self.candidate_fixture()
        result = self.run_candidate_dry_run(candidate, digests)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("PLANNED", report["status"])
        self.assertEqual("b" * 40, report["candidate"]["sourceSha"])
        self.assertEqual(digests[0], report["candidate"]["desktopManifestSha256"])
        self.assertEqual(digests[1], report["candidate"]["hostManifestSha256"])
        self.assertFalse(self.state.exists())
        self.assertFalse(self.binding.exists())

    def test_initialize_dry_run_refuses_wrong_source_or_manifest_hash(self):
        candidate, digests = self.candidate_fixture()
        for source, hashes in (("a" * 40, digests), ("b" * 40, ["0" * 64, digests[1]])):
            with self.subTest(source=source, hashes=hashes):
                result = self.run_candidate_dry_run(candidate, hashes, source)
                self.assertEqual(2, result.returncode, result.stderr)
                self.assertEqual("LocalSimulation.CandidateArtifactMismatch", json.loads(result.stdout)["code"])
                self.assertFalse(self.state.exists())

    def test_initialize_dry_run_refuses_extra_missing_or_modified_payload(self):
        candidate, digests = self.candidate_fixture()
        binary = candidate / "desktop" / "MyDmsVn.Desktop.App.exe"
        additional = candidate / "host" / "unexpected.dll"
        additional.write_bytes(b"unexpected")
        result = self.run_candidate_dry_run(candidate, digests)
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertEqual("LocalSimulation.CandidateArtifactMismatch", json.loads(result.stdout)["code"])
        additional.unlink()
        binary.write_bytes(b"modified")
        result = self.run_candidate_dry_run(candidate, digests)
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertEqual("LocalSimulation.CandidateArtifactMismatch", json.loads(result.stdout)["code"])
        binary.unlink()
        result = self.run_candidate_dry_run(candidate, digests)
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertEqual("LocalSimulation.CandidateArtifactMismatch", json.loads(result.stdout)["code"])
        self.assertFalse(self.state.exists())

    def test_initialize_dry_run_refuses_unsafe_manifest_path(self):
        candidate, digests = self.candidate_fixture(extra_path="../keep.txt")
        result = self.run_candidate_dry_run(candidate, digests)
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertEqual("LocalSimulation.CandidateArtifactMismatch", json.loads(result.stdout)["code"])
        self.assertEqual("unrelated", (self.outside / "keep.txt").read_text(encoding="utf-8"))
        self.assertFalse(self.state.exists())


    def test_prebuilt_copy_returns_the_expected_host_manifest_hash(self):
        candidate, digests = self.candidate_fixture()
        host = candidate / "host"
        rows = [f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}"
                for path in sorted(host.iterdir(), key=lambda item: item.name)]
        hash_expected = hashlib.sha256(("\n".join(rows) + "\n").encode("ascii")).hexdigest()
        destination = self.outside / "staged"
        destination.mkdir()
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"$pair = Get-LocalSimulationCandidate '{candidate}' '{'b' * 40}' '{digests[0]}' '{digests[1]}'; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($candidate,$dest) "
            "Copy-LocalSimulationCandidate $candidate $dest "
            f"}} $pair '{destination}'"
        )
        result = subprocess.run(
            [powershell(), "-NoProfile", "-Command", command], env=self.process_env,
            text=True, capture_output=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(hash_expected, result.stdout.strip())
        self.assertEqual(hash_expected, hashlib.sha256((destination / "host" / "artifact-manifest.txt").read_bytes()).hexdigest())
        self.assertEqual(
            (candidate / "desktop" / "MyDmsVn.Desktop.App.exe").read_bytes(),
            (destination / "desktop" / "MyDmsVn.Desktop.App.exe").read_bytes(),
        )

    @unittest.skipUnless(os.name == "nt", "Windows junction verification")
    def test_initialize_dry_run_refuses_candidate_reparse_ancestor(self):
        candidate, digests = self.candidate_fixture()
        junction = self.outside / "junction"
        result = subprocess.run(
            ["cmd.exe", "/d", "/c", "mklink", "/J", str(junction), str(candidate)],
            text=True, capture_output=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        try:
            result = self.run_candidate_dry_run(junction, digests)
            self.assertEqual(2, result.returncode, result.stderr)
            self.assertEqual("LocalSimulation.CandidateArtifactMismatch", json.loads(result.stdout)["code"])
            self.assertFalse(self.state.exists())
        finally:
            junction.rmdir()

    @unittest.skipUnless(os.name == "nt", "Windows junction verification")
    def test_prebuilt_copy_refuses_destination_junction_before_touching_target(self):
        candidate, digests = self.candidate_fixture()
        destination = self.outside / "staged"
        destination.mkdir()
        unrelated = self.outside / "unrelated"
        unrelated.mkdir()
        junction = destination / "desktop"
        created = subprocess.run(
            ["cmd.exe", "/d", "/c", "mklink", "/J", str(junction), str(unrelated)],
            text=True, capture_output=True,
        )
        self.assertEqual(0, created.returncode, created.stderr)
        try:
            command = (
                "$ErrorActionPreference='Stop'; "
                f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
                f"$pair = Get-LocalSimulationCandidate '{candidate}' '{'b' * 40}' '{digests[0]}' '{digests[1]}'; "
                f"& (Get-Module LocalSimulation.Runtime) {{ param($candidate,$dest) "
                "Copy-LocalSimulationCandidate $candidate $dest "
                f"}} $pair '{destination}'"
            )
            result = subprocess.run(
                [powershell(), "-NoProfile", "-Command", command], env=self.process_env,
                text=True, capture_output=True,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertEqual([], list(unrelated.iterdir()), result.stdout + result.stderr)
        finally:
            junction.rmdir()

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
        expected_prefix = "/mnt/" + ROOT.drive[0].lower() + "/"
        self.assertTrue(result.stdout.strip().startswith(expected_prefix), result.stdout)

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

    @unittest.skipUnless(os.name == "nt", "Windows ACL behavior is Windows-specific")
    def test_registration_accepts_system_and_builtin_administrators(self):
        receipt = self.write_receipt()
        self.write_binding(receipt)
        for sid in ("*S-1-5-18", "*S-1-5-32-544"):
            changed = subprocess.run(
                ["icacls.exe", str(self.binding), "/grant", sid + ":F"],
                env=self.process_env,
                text=True,
                capture_output=True,
            )
            self.assertEqual(0, changed.returncode, changed.stdout + changed.stderr)
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            f"& (Get-Module LocalSimulation.Runtime) {{ param($s,$r) Assert-LocalSimulationRegistration $s ($r|ConvertFrom-Json) }} "
            f"'{self.state}' '{json.dumps(receipt)}'"
        )

        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

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

    def test_http_exercise_replays_same_request_and_rejects_changed_payload(self):
        command = (
            f"Import-Module '{RUNTIME_MODULE}' -Force -DisableNameChecking; "
            "& (Get-Module LocalSimulation.Runtime) { "
            "$script:calls=[Collections.Generic.List[string]]::new(); "
            "$send={ param($uri,$headers,$body) $script:calls.Add($body); "
            "if($script:calls.Count -lt 3){ [pscustomobject]@{StatusCode=200;Body=[pscustomobject]@{data=[pscustomobject]@{saleId='42'}}} } "
            "else { [pscustomobject]@{StatusCode=409;Body=[pscustomobject]@{error=[pscustomobject]@{code='Idempotency.KeyConflict'}}} } }; "
            "$result=Invoke-IdempotentSalesExercise -Uri 'https://localhost:50443/api/v1/sales-receipts' "
            "-Headers @{} -Body '{\"note\":\"original\"}' -ChangedBody '{\"note\":\"changed\"}' -Send $send; "
            "[pscustomobject]@{saleId=$result.data.saleId;calls=@($script:calls)}|ConvertTo-Json -Compress }"
        )

        result = subprocess.run(
            [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
            cwd=ROOT,
            env=self.process_env,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("42", report["saleId"])
        self.assertEqual(
            ['{"note":"original"}', '{"note":"original"}', '{"note":"changed"}'],
            report["calls"],
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

    def test_default_reset_deletes_only_active_instance_and_preserves_parent_sibling(self):
        isolated_repo = self.outside / "isolated-repo"
        isolated_scripts = isolated_repo / "scripts" / "local-simulation"
        isolated_scripts.mkdir(parents=True)
        isolated_operator = isolated_scripts / SCRIPT.name
        isolated_runtime = isolated_scripts / RUNTIME_MODULE.name
        shutil.copy2(SCRIPT, isolated_operator)
        shutil.copy2(RUNTIME_MODULE, isolated_runtime)
        active_state = isolated_repo / "TestResults" / "local-simulation" / "active"
        sibling = active_state.parent / "evidence" / "keep.txt"
        sibling.parent.mkdir(parents=True)
        sibling.write_text("must-remain", encoding="utf-8")
        original_state = self.state
        self.state = active_state
        try:
            receipt = self.write_receipt()
            command = (
                f"Import-Module '{isolated_runtime}' -Force -DisableNameChecking; "
                f"& (Get-Module LocalSimulation.Runtime) {{ param($s) $p=Join-Path $s 'receipt.json'; "
                "$r=Get-Content $p -Raw|ConvertFrom-Json; New-LocalSimulationRegistration $s $r $p } "
                f"'{active_state}'"
            )
            registered = subprocess.run(
                [powershell(), "-NoLogo", "-NoProfile", "-Command", command],
                cwd=isolated_repo,
                env=self.process_env,
                text=True,
                capture_output=True,
            )
            self.assertEqual(0, registered.returncode, registered.stdout + registered.stderr)

            result = subprocess.run(
                [
                    powershell(), "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
                    "-File", str(isolated_operator), "-Action", "Reset",
                    "-ConfirmInstanceId", receipt["instanceId"], "-OutputFormat", "Json",
                ],
                cwd=isolated_repo,
                env=self.process_env,
                text=True,
                capture_output=True,
            )

            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertFalse(active_state.exists())
            self.assertEqual("must-remain", sibling.read_text(encoding="utf-8"))
        finally:
            self.state = original_state

    def test_runbook_paths_match_the_default_active_state_root(self):
        runbook = (ROOT / "docs" / "runbooks" / "local-fullstack-simulation.md").read_text(
            encoding="utf-8"
        )
        operator = SCRIPT.read_text(encoding="utf-8")
        self.assertIn("$StateRoot = Join-Path $allowedStateParent 'active'", operator)
        expected_paths = (
            "TestResults/local-simulation/active/receipt.json",
            "TestResults/local-simulation/active/secrets",
            r"TestResults\local-simulation\active\receipt.json",
            r"TestResults\local-simulation\active\artifacts\desktop\MyDmsVn.Desktop.App.exe",
        )
        for path in expected_paths:
            with self.subTest(path=path):
                self.assertIn(path, runbook)
        stale_paths = (
            "TestResults/local-simulation/receipt.json",
            "TestResults/local-simulation/secrets",
            r"TestResults\local-simulation\receipt.json",
            r"TestResults\local-simulation\artifacts\desktop\MyDmsVn.Desktop.App.exe",
        )
        for path in stale_paths:
            with self.subTest(stale=path):
                self.assertNotIn(path, runbook)

    def test_explicit_parent_state_root_is_refused_for_initialize_and_reset(self):
        STATE_PARENT.mkdir(parents=True, exist_ok=True)
        sibling = STATE_PARENT / ("review-evidence-" + uuid.uuid4().hex)
        sibling.write_text("keep", encoding="utf-8")
        try:
            for action, extra in (("Initialize", ["-DryRun"]), ("Reset", [])):
                with self.subTest(action=action):
                    result = subprocess.run(
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
                            str(STATE_PARENT),
                            "-OutputFormat",
                            "Json",
                            *extra,
                        ],
                        cwd=ROOT,
                        env=self.process_env,
                        text=True,
                        capture_output=True,
                    )
                    self.assertEqual(2, result.returncode, result.stdout + result.stderr)
                    self.assertEqual(
                        "LocalSimulation.StateRootOutsideAllowedParent",
                        json.loads(result.stdout)["code"],
                    )
                    self.assertEqual("keep", sibling.read_text(encoding="utf-8"))
        finally:
            sibling.unlink(missing_ok=True)

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
