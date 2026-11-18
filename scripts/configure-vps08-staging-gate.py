#!/usr/bin/env python3
"""Operator-only GitHub Environment config for VPS-08; never reads secret values.

Requires authenticated local 'gh' CLI with repository Administration/Environments rights.
A passing verify is only Environment configuration evidence, never SQL/VPS acceptance.
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys

REPO = "chung6a8m/MyDmsVn-Vps"
ENV = "vps08-sql13-nonproduction"
API = f"repos/{REPO}/environments/{ENV}"
EXPECTED_POLICY = "main"
SECRET_NAMES = {"VPS08_STAGING_HOST_ATTESTATION"}
VARIABLE_NAMES = {"VPS08_STAGING_PROBE_URL", "VPS08_STAGING_MACHINE_SHA256"}


class Blocked(Exception):
    pass


def gh(*parts: str, data: dict | None = None) -> str:
    if shutil.which("gh") is None:
        raise Blocked("Install and authenticate gh CLI before configuring Environment.")
    args = ["gh", *parts]
    if data is not None:
        args += ["--input", "-"]
    proc = subprocess.run(args, input=json.dumps(data) if data is not None else None,
                          text=True, capture_output=True, check=False)
    if proc.returncode:
        # Never echo gh stderr: it may contain sensitive configuration.
        raise Blocked(f"GitHub API action refused (exit={proc.returncode}).")
    return proc.stdout


def get(path: str) -> dict:
    response = json.loads(gh("api", path))
    if not isinstance(response, dict):
        raise Blocked("Unexpected API shape.")
    return response


def branch_policies() -> list[dict]:
    page = get(f"{API}/deployment-branch-policies?per_page=100")
    policies = page.get("branch_policies", [])
    if not isinstance(policies, list) or page.get("total_count") != len(policies):
        raise Blocked("Paginated/invalid environment policy set.")
    return policies


def configure(reviewer: str) -> None:
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]{0,38}", reviewer):
        raise Blocked("Provide an explicit independent GitHub reviewer login.")
    operator, user = get("user"), get(f"users/{reviewer}")
    if operator.get("id") == user.get("id"):
        raise Blocked("Same-account approval is forbidden.")
    gh("api", "--method", "PUT", API, data={
        "wait_timer": 0,
        "prevent_self_review": True,
        "reviewers": [{"type": "User", "id": user["id"]}],
        "deployment_branch_policy": {
            "protected_branches": False,
            "custom_branch_policies": True,
        },
    })
    found = {item.get("name") for item in branch_policies()}
    if found not in (set(), {EXPECTED_POLICY}):
        raise Blocked("Unknown existing deployment policy: refuse to widen/delete.")
    if not found:
        gh("api", "--method", "POST", f"{API}/deployment-branch-policies",
           data={"name": EXPECTED_POLICY, "type": "branch"})
    print("VPS08_ENV_CONFIGURED: independent reviewer and exact main policy.")
    print("Disable admin bypass manually in GitHub Environment settings before verify.")


def verify(expected_sha: str | None) -> None:
    environment = get(API)
    if environment.get("can_admins_bypass") is not False:
        raise Blocked("Admin bypass must be DISABLED in GitHub Environment settings.")
    policy = environment.get("deployment_branch_policy") or {}
    if policy.get("custom_branch_policies") is not True or policy.get("protected_branches") is not False:
        raise Blocked("Environment must enforce exact custom branch policy.")
    rules = [x for x in environment.get("protection_rules", []) if x.get("type") == "required_reviewers"]
    if len(rules) != 1 or len(rules[0].get("reviewers", [])) != 1:
        raise Blocked("Require exactly one independent reviewer.")
    if not rules[0].get("prevent_self_review", environment.get("prevent_self_review", False)):
        raise Blocked("Prevent self-review must be enabled.")
    if {item.get("name") for item in branch_policies()} != {EXPECTED_POLICY}:
        raise Blocked("Only exact main may deploy to protected staging.")
    current_id = get("user").get("id")
    for reviewer in rules[0]["reviewers"]:
        if (reviewer.get("reviewer") or {}).get("id") == current_id:
            raise Blocked("Operator is configured as their own reviewer.")
    secrets = get(f"{API}/secrets?per_page=100")
    variables = get(f"{API}/variables?per_page=100")
    if (secrets.get("total_count") != len(secrets.get("secrets", [])) or
            variables.get("total_count") != len(variables.get("variables", []))):
        raise Blocked("Environment secrets/vars are paginated; cannot prove names.")
    missing_secret = SECRET_NAMES - {x.get("name") for x in secrets.get("secrets", [])}
    missing_vars = VARIABLE_NAMES - {x.get("name") for x in variables.get("variables", [])}
    if missing_secret or missing_vars:
        raise Blocked("Missing secret/variable names: " +
                      ", ".join(sorted(missing_secret | missing_vars)))
    sha = get(f"repos/{REPO}/branches/main")["commit"]["sha"]
    if expected_sha is not None and (not re.fullmatch(r"[0-9a-f]{40}", expected_sha) or sha != expected_sha):
        raise Blocked("Reviewed main HEAD changed; fresh review required.")
    print(f"VPS08_ENV_CONFIG_PASS: main_sha={sha}; independent_review=required; bypass=disabled; names=present")
    print("NOT VERIFIED: actual SQL isolation, TLS, restricted principal, independent Oracle, runner, reboot, rollback.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    actions.add_parser("configure").add_argument("--reviewer", required=True)
    actions.add_parser("verify").add_argument("--expected-sha")
    args = parser.parse_args()
    try:
        if args.action == "configure":
            configure(args.reviewer)
        else:
            verify(args.expected_sha)
        return 0
    except (Blocked, KeyError, ValueError, TypeError, json.JSONDecodeError) as err:
        print(f"VPS08_ENV_BLOCKED: {err}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
