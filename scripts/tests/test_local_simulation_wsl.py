import json
import os
import pathlib
import shutil
import subprocess
import tempfile
import unittest
import uuid


ROOT = pathlib.Path(__file__).resolve().parents[2]
HELPER = ROOT / "scripts" / "local-simulation" / "mydmsvn-local-sim"


def linux_path(path):
    path = pathlib.Path(path).resolve()
    if os.name != "nt":
        return str(path)
    drive = path.drive[0].lower()
    return "/mnt/" + drive + str(path)[2:].replace("\\", "/")


def run_helper(*args):
    command = ["bash", linux_path(HELPER), *[linux_path(v) if isinstance(v, pathlib.Path) else str(v) for v in args]]
    if os.name == "nt":
        command = ["wsl", "-d", "Ubuntu", "--", *command]
    return subprocess.run(command, cwd=ROOT, text=True, capture_output=True)


class LocalSimulationWslTests(unittest.TestCase):
    def test_prerequisites_use_a_hash_pinned_aspnet_runtime(self):
        source = HELPER.read_text(encoding="utf-8")
        self.assertIn(
            "aspnetcore/Runtime/10.0.11/aspnetcore-runtime-10.0.11-linux-x64.tar.gz",
            source,
        )
        self.assertIn("4c6be0623330074e699dab8084be15a1", source)
        self.assertIn("sha512sum --check --status", source)
        self.assertNotIn("curl |", source)

    def test_install_and_reset_own_the_wsl_ca_trust_entry(self):
        source = HELPER.read_text(encoding="utf-8")
        self.assertGreaterEqual(source.count("mydmsvn-local-sim-ca.crt"), 2)
        self.assertIn("update-ca-certificates", source)
        self.assertIn("/opt/mydmsvn-local-sim/runtime", source)

    def test_install_refuses_preexisting_system_resources(self):
        source = HELPER.read_text(encoding="utf-8")
        self.assertIn("admit_lab", source)
        self.assertIn("id mydmsvn-local-sim", source)
        for path in (
            "/opt/mydmsvn-local-sim",
            "/etc/mydmsvn-local-sim",
            "/var/lib/mydmsvn-local-sim",
            "/etc/systemd/system/mydmsvn-local-sim.service",
        ):
            self.assertIn(path, source)

    def test_install_claims_identity_before_user_and_runtime_acquisition(self):
        source = HELPER.read_text(encoding="utf-8")
        install = source.split("install_lab()", 1)[1].split("install_prerequisites()", 1)[0]
        marker = install.index("/etc/mydmsvn-local-sim/instance-id")
        self.assertLess(marker, install.index("useradd --system"))
        self.assertLess(marker, install.index('cp -a -- "$state/runtime"'))
        self.assertIn("/etc/mydmsvn-local-sim/source-sha", install)

    def test_reset_keeps_marker_until_cleanup_postconditions(self):
        source = HELPER.read_text(encoding="utf-8")
        reset = source.split("reset_lab()", 1)[1].split("reset_sandbox()", 1)[0]
        self.assertIn("LocalSimulation.ResetPostconditionFailed", reset)
        self.assertIn("LocalSimulation.UserRemoveFailed", reset)
        self.assertGreater(reset.rindex("rm -rf -- /etc/mydmsvn-local-sim"), reset.index("systemctl reload nginx"))

    def test_verify_has_a_bounded_service_readiness_wait(self):
        source = HELPER.read_text(encoding="utf-8")
        self.assertIn("for _ in $(seq 1 30)", source)
        self.assertIn("LocalSimulation.HttpsHealthFailed", source)

    def test_recovery_requires_a_new_main_pid_and_https(self):
        source = HELPER.read_text(encoding="utf-8")
        self.assertGreaterEqual(source.count("systemctl reset-failed mydmsvn-local-sim.service"), 2)
        self.assertIn("systemctl reset-failed mydmsvn-local-sim.service", source)
        self.assertIn("systemctl kill --kill-who=main --signal=SIGKILL", source)
        self.assertIn('"$current" != "$before"', source)
        self.assertIn('recovery_result "$recovered" "$https"', source)

    def setUp(self):
        self.base = pathlib.Path(tempfile.mkdtemp(prefix="mydmsvn-local-sim-test-"))
        self.state = self.base / "state"
        self.output = self.base / "rendered"
        self.publish = self.base / "publish"
        for path in (self.state / "secrets", self.state / "tls", self.publish):
            path.mkdir(parents=True, exist_ok=True)
        self.instance_id = "11111111-2222-4333-8444-555555555555"
        (self.state / "instance.env").write_text(
            "\n".join(
                [
                    f"INSTANCE_ID={self.instance_id}",
                    "SOURCE_SHA=" + "a" * 40,
                    "PUBLIC_ORIGIN=https://localhost:50443",
                    "SQL_SERVER=127.0.0.1,11433",
                ]
            )
            + "\n",
            encoding="utf-8",
        )
        for name, value in {
            "proxy_token": "ProxyToken_1234567890_abcdef",
            "jwt_keys": "local-key:JwtKey_1234567890_abcdef",
            "sql_connection_string": "Server=127.0.0.1,11433;Password=PrivateSql_123!",
        }.items():
            (self.state / "secrets" / name).write_text(value, encoding="utf-8")
        for name in ("ca.crt", "nginx.crt", "nginx.key"):
            (self.state / "tls" / name).write_text("fixture-" + name, encoding="utf-8")
        (self.publish / "MyDmsVn.Server.AspNetCoreHost.dll").write_text("fixture", encoding="utf-8")

    def tearDown(self):
        shutil.rmtree(self.base, ignore_errors=True)

    def test_render_produces_hardened_local_service_and_proxy_without_secrets(self):
        result = run_helper("render", self.state, self.output, self.publish)

        self.assertEqual(0, result.returncode, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("RENDERED", report["status"])
        service = (self.output / "mydmsvn-local-sim.service").read_text(encoding="utf-8")
        nginx = (self.output / "mydmsvn-local-sim.nginx.conf").read_text(encoding="utf-8")
        wrapper = (self.output / "mydmsvn-local-sim-start").read_text(encoding="utf-8")
        combined = service + nginx + wrapper
        self.assertIn("ProtectSystem=strict", service)
        self.assertIn("127.0.0.1:5080", service)
        self.assertIn("listen 127.0.0.1:50443 ssl", nginx)
        self.assertIn("proxy_set_header X-Forwarded-Proto https", nginx)
        self.assertIn("include /etc/nginx/mydmsvn-local-sim-proxy-token.conf", nginx)
        self.assertIn('$http_host != "localhost:50443"', nginx)
        self.assertNotIn("PrivateSql_123!", combined)
        self.assertNotIn("ProxyToken_1234567890_abcdef", combined)

    def test_render_refuses_invalid_instance_identity(self):
        instance_file = self.state / "instance.env"
        instance_file.write_text(
            instance_file.read_text(encoding="utf-8").replace(self.instance_id, "not-a-guid"),
            encoding="utf-8",
        )

        result = run_helper("render", self.state, self.output, self.publish)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("LocalSimulation.InstanceInvalid", result.stderr)
        self.assertFalse(self.output.exists())

    def test_sandbox_reset_requires_matching_marker_and_preserves_sibling(self):
        sandbox = self.base / "sandbox"
        owned = sandbox / "mydmsvn-local-sim-owned"
        sibling = sandbox / "unrelated"
        owned.mkdir(parents=True)
        sibling.mkdir(parents=True)
        (owned / "instance-id").write_text(self.instance_id, encoding="utf-8")
        (sibling / "keep").write_text("keep", encoding="utf-8")

        result = run_helper("reset-sandbox", owned, self.instance_id)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse(owned.exists())
        self.assertEqual("keep", (sibling / "keep").read_text(encoding="utf-8"))

    def test_recovery_result_fails_when_service_or_https_did_not_recover(self):
        passed = run_helper("recovery-result", "1", "1")
        failed = run_helper("recovery-result", "1", "0")

        self.assertEqual(0, passed.returncode)
        self.assertEqual("RECOVERED", json.loads(passed.stdout)["status"])
        self.assertNotEqual(0, failed.returncode)
        self.assertEqual("FAILED", json.loads(failed.stdout)["status"])


if __name__ == "__main__":
    unittest.main()
