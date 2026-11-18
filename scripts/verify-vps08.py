#!/usr/bin/env python3
"""VPS-08 deterministic source/package threat checks; NOT protected VPS evidence."""
from __future__ import annotations

import importlib.util
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LINUX = ROOT / "deployment/linux"
SCRIPT = ROOT / "scripts/vps08-manifest.py"
SHA = "a" * 40


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise AssertionError(reason)


def check_configs() -> None:
    unit = (LINUX / "mydmsvn-vps.service").read_text()
    nginx = (LINUX / "mydmsvn-nginx.conf.tpl").read_text()
    start = (LINUX / "mydmsvn-vps-start").read_text()
    rollout = (LINUX / "vps08-rollout").read_text()
    env = (LINUX / "runtime.env.example").read_text()
    for name in ("mydmsvn-vps-start", "vps08-rollout"):
        subprocess.run(["bash", "-n", str(LINUX / name)], check=True)
    for required in (
        "User=mydmsvn", "Group=mydmsvn", "ProtectSystem=strict",
        "NoNewPrivileges=true", "StateDirectoryMode=0700",
        "ASPNETCORE_URLS=http://127.0.0.1:5080",
        "MYDMSVN_VPS08_PROXY_MODE=loopback-nginx-v1",
        "LoadCredential=sql_connection_string:", "LoadCredential=jwt_keys:",
        "LoadCredential=proxy_token:", "ExecStart=/usr/local/libexec/mydmsvn-vps-start",
        "Restart=on-failure", "ReadWritePaths=/var/lib/mydmsvn/audit",
    ):
        require(required in unit, f"Missing systemd restriction: {required}")
    require("MYDMSVN_WEB_SQL_CONNECTION_STRING=" not in unit, "Secret in systemd unit")
    require("MYDMSVN_WEB_JWT_KEYS=" not in unit, "JWT secret in systemd unit")
    require("MYDMSVN_VPS08_PROXY_TOKEN=" not in unit, "Proxy secret in systemd unit")
    for required in (
        "listen 443 ssl;", "ssl_protocols TLSv1.2 TLSv1.3;",
        "proxy_pass http://127.0.0.1:5080;",
        "proxy_set_header X-Forwarded-For $remote_addr;",
        "proxy_set_header X-Forwarded-Proto https;",
        "include /etc/nginx/mydmsvn/proxy-token.conf;",
        'proxy_set_header Forwarded "";',
    ):
        require(required in nginx, f"Missing nginx policy: {required}")
    require("$proxy_add_x_forwarded_for" not in nginx, "Client forwarding chain not overwritten")
    require("proxy_ssl_verify off" not in nginx, "Unsafe upstream setting")
    for expected in ("load_credential MYDMSVN_WEB_SQL_CONNECTION_STRING",
                     "load_credential MYDMSVN_WEB_JWT_KEYS",
                     "load_credential MYDMSVN_VPS08_PROXY_TOKEN", "exec /usr/bin/dotnet"):
        require(expected in start, "Credential wrapper is incomplete")
    require("systemctl restart" in rollout and 'swap "$current"' in rollout,
            "Atomic rollback path missing")
    for item in ("trusted_dir /opt", 'trusted_dir "$root/releases"',
                 'flock -n 9 || abort', 'trusted_release "$root/$new_target" "$sha"',
                 'cmp -s -- "$source_dir/vps08-manifest.json"',
                 'systemctl is-active --quiet "$service"',
                 '[[ "$restart_ok" == 1 ]] && probe "$new_target"',
                 'if systemctl restart "$service" && probe "$current"',
                 'find "$root/$new_target" -type d -exec chmod 0755',
                 'find "$root/$new_target" -type f -exec chmod go-w,a+r'):
        require(item in rollout, "Missing rollout security guard: " + item)
    require('systemctl restart "$service" || true' not in rollout,
            "Never suppress a failed systemd restart during deploy or recovery")
    for forbidden in ("MYDMSVN_WEB_SQL_CONNECTION_STRING=", "MYDMSVN_WEB_JWT_KEYS=",
                      "MYDMSVN_VPS08_PROXY_TOKEN="):
        require(forbidden not in env, "Example environment must not contain secrets")
    print("VPS08_SOURCE_POLICY_PASS: systemd, nginx, credential isolation, rollback syntax")


def _extract_shell_function(source: str, name: str) -> str:
    marker = f"{name}() {{\n"
    require(source.count(marker) == 1, f"Expected one shell function: {name}")
    tail = source.split(marker, 1)[1]
    require("\n}\n" in tail, f"Unterminated shell function: {name}")
    return marker + tail.split("\n}\n", 1)[0] + "\n}\n"


def check_credential_loader() -> None:
    # Execute the actual production credential function, not just bash -n.
    source = (LINUX / "mydmsvn-vps-start").read_text()
    function = _extract_shell_function(source, "load_credential")
    shell = (
        "set -euo pipefail\n"
        "fail() { exit 17; }\n" + function +
        "load_credential SQL_VALUE sql_connection_string\n"
        "load_credential JWT_VALUE jwt_keys\n"
        "load_credential PROXY_VALUE proxy_token\n"
        '[[ "$SQL_VALUE" == "Server=sql.example;Encrypt=True;" ]]\n'
        '[[ "$JWT_VALUE" == "example-jwt-value" ]]\n'
        '[[ "$PROXY_VALUE" == "RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR" ]]\n'
        'printf "VPS08_CREDENTIAL_READ_PASS\n"\n'
    )
    with tempfile.TemporaryDirectory(prefix="vps08-credentials-") as directory:
        root = Path(directory)
        (root / "sql_connection_string").write_text("Server=sql.example;Encrypt=True;\n")
        (root / "jwt_keys").write_text("example-jwt-value")
        proxy = root / "proxy_token"
        proxy.write_text("R" * 32)
        env = {"PATH": "/usr/bin:/bin", "CREDENTIALS_DIRECTORY": directory}

        def execute() -> subprocess.CompletedProcess[str]:
            return subprocess.run(["bash", "-c", shell], env=env, text=True,
                                  capture_output=True, check=False)

        good = execute()
        require(good.returncode == 0 and good.stdout == "VPS08_CREDENTIAL_READ_PASS\n",
                "Valid independent systemd credential files were not loaded")
        proxy.write_text("R" * 32 + "\nsecond-value")
        invalid = execute()
        require(invalid.returncode != 0 and not invalid.stdout,
                "Embedded newline credential was accepted")
        proxy.unlink()
        outside = root / "outside"
        outside.write_text("R" * 32)
        proxy.symlink_to(outside)
        linked = execute()
        require(linked.returncode != 0 and not linked.stdout,
                "Symlink credential was accepted")
    print("VPS08_CREDENTIAL_LOADER_PASS: valid=3, injected-newline=denied, symlink=denied")


def check_rollout_process_identity() -> None:
    # Model a green public HTTPS probe while an *old* release still runs.
    # The rollout must reject it, and also reject missing systemd MainPID.
    source = (LINUX / "vps08-rollout").read_text()
    function = _extract_shell_function(source, "probe")
    shell = (
        "set -euo pipefail\n"
        "root=/opt/mydmsvn\n"
        "service=mydmsvn-vps.service\n"
        "probe_url=https://example.test/api/v1/health\n"
        'systemctl() { case "$1" in is-active) return 0;; show) printf "%s\n" "$SIM_PID";; *) return 1;; esac; }\n'
        'readlink() { printf "%s\n" "$SIM_CWD"; }\n'
        'curl() { return 0; }\n'
        'seq() { echo 1; }\n'
        'sleep() { :; }\n' + function +
        'probe "$SIM_EXPECTED"\n'
    )
    desired = "a" * 40
    approved_cwd = "/opt/mydmsvn/releases/" + desired
    for cwd, pid, allow in (
        (approved_cwd, "4242", True),
        ("/opt/mydmsvn/releases/" + "b" * 40, "4242", False),
        (approved_cwd, "0", False),
    ):
        env = {"PATH": "/usr/bin:/bin", "SIM_CWD": cwd, "SIM_PID": pid,
               "SIM_EXPECTED": "releases/" + desired}
        run = subprocess.run(["bash", "-c", shell], env=env, text=True,
                             capture_output=True, check=False)
        require((run.returncode == 0) == allow,
                "HTTPS/systemd probe was accepted without running expected release")
    print("VPS08_ROLLOUT_PID_GATE_PASS: expected_release=pass, stale_release=deny, no_pid=deny")


def check_artifact() -> None:
    spec = importlib.util.spec_from_file_location("vps08_manifest", SCRIPT)
    require(spec is not None and spec.loader is not None, "Manifest loader missing")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    with tempfile.TemporaryDirectory(prefix="vps08-manifest-") as directory:
        root = Path(directory)
        for name in module.NEEDED:
            (root / name).write_bytes(name.encode())
        module.process("create", directory, SHA)
        module.process("verify", directory, SHA)
        try:
            module.process("verify", directory, "b" * 40)
            raise AssertionError("Wrong source SHA accepted")
        except ValueError:
            pass
        dll = root / "MyDmsVn.Server.AspNetCoreHost.dll"
        dll.write_bytes(b"tampered")
        try:
            module.process("verify", directory, SHA)
            raise AssertionError("Modified DLL accepted")
        except ValueError:
            pass
        dll.write_bytes(dll.name.encode())
        (root / "link").symlink_to(dll)
        try:
            module.process("verify", directory, SHA)
            raise AssertionError("Symlink accepted")
        except ValueError:
            pass
        (root / "link").unlink()
        (root / ".env.production").write_text("placeholder")
        try:
            module.process("verify", directory, SHA)
            raise AssertionError("Env artifact accepted")
        except ValueError:
            pass
        (root / ".env.production").unlink()
        dll.chmod(0o666)
        try:
            module.process("verify", directory, SHA)
            raise AssertionError("World-writable DLL accepted")
        except ValueError:
            pass
        dll.chmod(0o644)
        nested = root / "writable-dir"
        nested.mkdir()
        nested.chmod(0o777)
        try:
            module.process("verify", directory, SHA)
            raise AssertionError("World-writable nested directory accepted")
        except ValueError:
            pass
    print("VPS08_ARTIFACT_NEGATIVE_PASS: wrong SHA, mutation, symlink, env, unsafe modes")


if __name__ == "__main__":
    check_configs()
    check_artifact()
    check_credential_loader()
    check_rollout_process_identity()
