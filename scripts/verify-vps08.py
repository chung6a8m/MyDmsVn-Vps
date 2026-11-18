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
                 'find "$root/$new_target" -type d -exec chmod 0755',
                 'find "$root/$new_target" -type f -exec chmod go-w,a+r'):
        require(item in rollout, "Missing rollout security guard: " + item)
    for forbidden in ("MYDMSVN_WEB_SQL_CONNECTION_STRING=", "MYDMSVN_WEB_JWT_KEYS=",
                      "MYDMSVN_VPS08_PROXY_TOKEN="):
        require(forbidden not in env, "Example environment must not contain secrets")
    print("VPS08_SOURCE_POLICY_PASS: systemd, nginx, credential isolation, rollback syntax")


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
