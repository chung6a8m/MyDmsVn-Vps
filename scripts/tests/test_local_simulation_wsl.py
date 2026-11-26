import hashlib
import json
import os
import pathlib
import shlex
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
    def test_installed_payload_permissions_drop_windows_mount_write_bits(self):
        driver = self.base / "install-file-modes.sh"
        driver.write_text(
            'source "$1"\n'
            'root="$(mktemp -d /tmp/mydmsvn-file-modes.XXXXXXXX)"\n'
            'trap \'rm -f -- "$root/dotnet" "$root/host.dll"; rmdir -- "$root"\' EXIT\n'
            'touch "$root/dotnet" "$root/host.dll"\n'
            'chmod 0777 "$root" "$root/dotnet" "$root/host.dll"\n'
            'protect_installation_tree "$root"\n'
            'chmod 0755 -- "$root/dotnet"\n'
            'stat -c %a "$root" "$root/dotnet" "$root/host.dll"\n',
            encoding="utf-8", newline="\n",
        )
        command = ["bash", linux_path(driver), linux_path(HELPER)]
        if os.name == "nt":
            command = ["wsl", "-d", "Ubuntu", "--", *command]
        result = subprocess.run(command, cwd=ROOT, text=True, capture_output=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["755", "755", "644"], result.stdout.split())

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
        self.assertLess(marker, install.index('cp -R --no-preserve=ownership,mode -- "$state/runtime/."'))
        self.assertIn("/etc/mydmsvn-local-sim/source-sha", install)

    def test_reset_keeps_marker_until_cleanup_postconditions(self):
        source = HELPER.read_text(encoding="utf-8")
        reset = source.split("reset_lab()", 1)[1].split("reset_sandbox()", 1)[0]
        self.assertIn("LocalSimulation.ResetPostconditionFailed", reset)
        self.assertIn("LocalSimulation.UserRemoveFailed", reset)
        self.assertGreater(reset.rindex("rm -rf -- /etc/mydmsvn-local-sim"), reset.index("reload_nginx_after_reset"))

    def test_reset_does_not_reload_or_start_inactive_nginx(self):
        calls = self.base / "nginx-systemctl-calls"
        script = rf'''
set -euo pipefail
helper={shlex.quote(linux_path(HELPER))}
call_log={shlex.quote(linux_path(calls))}
systemctl() {{
  printf 'systemctl %s\n' "$*" >>"$call_log"
  if [[ "$1" == "is-active" ]]; then
    return 3
  fi
  return 97
}}
nginx() {{
  printf 'nginx %s\n' "$*" >>"$call_log"
  return 0
}}
source "$helper"
reload_nginx_after_reset
'''
        driver = self.base / "inactive-nginx-reset-test.sh"
        driver.write_text(script, encoding="utf-8", newline="\n")
        command = ["bash", linux_path(driver)]
        if os.name == "nt":
            command = ["wsl", "-d", "Ubuntu", "--", *command]

        result = subprocess.run(command, cwd=ROOT, text=True, capture_output=True)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(
            ["nginx -t", "systemctl is-active --quiet nginx"],
            calls.read_text(encoding="utf-8").splitlines(),
        )

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
        self.release_roots = []
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
        for root in self.release_roots:
            self.run_release_shell(
                '[[ "$1" == /tmp/mydmsvn-vps108-release.* && "$(realpath "$1")" == "$1" ]] '
                '&& rm -rf --one-file-system -- "$1"',
                root,
            )
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

    def run_release_shell(self, code, *args):
        driver = self.base / "release-driver.sh"
        driver.write_text('source "$1"\nshift\n' + code + "\n", encoding="utf-8", newline="\n")
        command = ["bash", linux_path(driver), linux_path(HELPER)]
        command.extend(linux_path(arg) if isinstance(arg, pathlib.Path) else str(arg) for arg in args)
        if os.name == "nt":
            command = ["wsl", "-d", "Ubuntu", "--", *command]
        return subprocess.run(command, cwd=ROOT, text=True, capture_output=True)

    def make_release_pair(self):
        created = self.run_release_shell('mktemp -d /tmp/mydmsvn-vps108-release.XXXXXXXX')
        self.assertEqual(0, created.returncode, created.stderr)
        root = created.stdout.strip()
        self.release_roots.append(root)
        old, new = "a" * 40, "b" * 40
        setup = self.run_release_shell(
            'for release in "$2" "$3"; do '
            '  mkdir -p "$1/releases/$release"; '
            '  printf %s "$release" >"$1/releases/$release/host.dll"; '
            '  (cd "$1/releases/$release" && sha256sum host.dll >artifact-manifest.txt); '
            'done; ln -s "releases/$2" "$1/current"',
            root, old, new,
        )
        self.assertEqual(0, setup.returncode, setup.stderr)
        hashes = [
            hashlib.sha256(f"{hashlib.sha256(release.encode()).hexdigest()}  host.dll\n".encode()).hexdigest()
            for release in (old, new)
        ]
        return root, old, new, hashes

    def switch_release(self, root, old, new, old_hash, new_hash):
        return self.run_release_shell('switch_candidate_release "$@"', root, old, new, old_hash, new_hash)

    def test_release_pointer_switch_is_retriable_and_rolls_back_to_verified_previous_release(self):
        root, old, new, hashes = self.make_release_pair()
        before = self.run_release_shell('readlink "$1/current"', root)
        self.assertEqual("releases/" + old, before.stdout.strip())
        first = self.switch_release(root, old, new, *hashes)
        self.assertEqual(0, first.returncode, first.stderr)
        self.assertEqual("SWITCHED", first.stdout.strip())
        self.assertEqual("releases/" + new, self.run_release_shell('readlink "$1/current"', root).stdout.strip())
        repeated = self.switch_release(root, old, new, *hashes)
        self.assertEqual(0, repeated.returncode, repeated.stderr)
        self.assertEqual("ALREADY_ACTIVE", repeated.stdout.strip())
        rollback = self.switch_release(root, new, old, hashes[1], hashes[0])
        self.assertEqual(0, rollback.returncode, rollback.stderr)
        self.assertEqual("releases/" + old, self.run_release_shell('readlink "$1/current"', root).stdout.strip())
        retained = self.run_release_shell('cat "$1/releases/$2/host.dll"', root, new)
        self.assertEqual(new, retained.stdout)

    def test_rollback_cancels_a_delayed_upgrade_worker_for_the_same_request(self):
        root, old, new, hashes = self.make_release_pair()
        arguments = (root, "a" * 32, old + ":" + hashes[0], new + ":" + hashes[1])
        ready = self.run_release_shell('record_upgrade_intent "$1" "$2" Upgrade "$3" "$4"', *arguments)
        self.assertEqual(0, ready.returncode, ready.stderr)
        rollback = self.run_release_shell('record_upgrade_intent "$1" "$2" RollbackUpgrade "$3" "$4"', *arguments)
        self.assertEqual(0, rollback.returncode, rollback.stderr)
        delayed = self.run_release_shell('record_upgrade_intent "$1" "$2" Upgrade "$3" "$4"', *arguments)
        self.assertEqual(2, delayed.returncode, delayed.stderr)
        self.assertIn("LocalSimulation.UpgradeIntentCancelled", delayed.stderr)
        self.assertEqual("releases/" + old, self.run_release_shell('readlink "$1/current"', root).stdout.strip())

    def test_release_pointer_retains_old_release_when_candidate_content_drifts(self):
        root, old, new, hashes = self.make_release_pair()
        changed = self.run_release_shell('printf changed >"$1/releases/$2/host.dll"', root, new)
        self.assertEqual(0, changed.returncode, changed.stderr)
        result = self.switch_release(root, old, new, *hashes)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("LocalSimulation.ArtifactIdentityMismatch", result.stderr)
        self.assertEqual("releases/" + old, self.run_release_shell('readlink "$1/current"', root).stdout.strip())

    def test_release_pointer_refuses_a_foreign_active_target(self):
        root, old, new, hashes = self.make_release_pair()
        target = "releases/" + "c" * 40
        setup = self.run_release_shell('ln -sfnT "$2" "$1/current"', root, target)
        self.assertEqual(0, setup.returncode, setup.stderr)
        result = self.switch_release(root, old, new, *hashes)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("LocalSimulation.ReleaseIdentityMismatch", result.stderr)
        self.assertEqual(target, self.run_release_shell('readlink "$1/current"', root).stdout.strip())

    def test_release_pointer_refuses_unsafe_manifest_path_even_when_hash_matches(self):
        root, old, new, hashes = self.make_release_pair()
        manifest = ("0" * 64 + "  ../host.dll\n").encode("ascii")
        changed = self.run_release_shell(
            'printf "%s\n" "$3" >"$1/releases/$2/artifact-manifest.txt"',
            root, new, manifest.decode("ascii").strip(),
        )
        self.assertEqual(0, changed.returncode, changed.stderr)
        result = self.switch_release(root, old, new, hashes[0], hashlib.sha256(manifest).hexdigest())
        self.assertNotEqual(0, result.returncode)
        self.assertIn("LocalSimulation.ArtifactIdentityMismatch", result.stderr)
        self.assertEqual("releases/" + old, self.run_release_shell('readlink "$1/current"', root).stdout.strip())

    def test_recovery_result_fails_when_service_or_https_did_not_recover(self):
        passed = run_helper("recovery-result", "1", "1")
        failed = run_helper("recovery-result", "1", "0")

        self.assertEqual(0, passed.returncode)
        self.assertEqual("RECOVERED", json.loads(passed.stdout)["status"])
        self.assertNotEqual(0, failed.returncode)
        self.assertEqual("FAILED", json.loads(failed.stdout)["status"])


if __name__ == "__main__":
    unittest.main()
