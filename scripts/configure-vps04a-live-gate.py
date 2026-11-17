#!/usr/bin/env python3
"""Fail-closed configuration for PR #9's protected Linux HTTPS→SQL acceptance.

Use an authenticated local GitHub CLI with Administration/Environments rights.
No secret values are accepted, printed, saved, or retrieved by this script.
Set environment secrets with `gh secret set --env ...` on the operator's device.
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys

REPO = "chung6a8m/MyDmsVn-Vps"
ENV = "vps04a-sql-nonproduction"
BRANCH = "codex/vps-04a-net10-aspnetcore"
PR = 9
LABEL = "vps04a-live-approved"
SECRETS = frozenset({
    "VPS04A_RUNTIME_SQL", "VPS04A_ORACLE_SQL", "VPS04A_SUPERUSER_USERNAME",
    "VPS04A_JWT_AUDIENCE", "VPS04A_JWT_KID", "VPS04A_JWT_KEYS",
    "VPS04A_WRITER_USERNAME", "VPS04A_WRITER_PASSWORD",
    "VPS04A_DENIED_USERNAME", "VPS04A_DENIED_PASSWORD",
})
VARIABLES = frozenset({
    "VPS04A_CUSTOMER_ID", "VPS04A_WAREHOUSE_ID",
    "VPS04A_EMPLOYEE_ID", "VPS04A_PRODUCT_ID",
})
ENV_API = f"repos/{REPO}/environments/{ENV}"


class AdmissionError(Exception):
    pass


def gh(*args: str, data: dict | None = None) -> str:
    if shutil.which("gh") is None:
        raise AdmissionError("Install GitHub CLI and run gh auth login locally first.")
    arguments = ["gh", *args]
    stdin = json.dumps(data) if data is not None else None
    if stdin is not None:
        arguments.extend(["--input", "-"])
    result = subprocess.run(arguments, text=True, input=stdin, capture_output=True, check=False)
    if result.returncode:
        # Never print stderr from a credential-bearing subprocess in CI output.
        raise AdmissionError(f"GitHub command failed (exit {result.returncode}); check gh auth status and admin access.")
    return result.stdout


def api_json(path: str) -> dict | list:
    return json.loads(gh("api", path))


def ensure_pr_head(expected: str | None = None) -> str:
    pr = api_json(f"repos/{REPO}/pulls/{PR}")
    if pr.get("state") != "open" or pr.get("merged_at") is not None:
        raise AdmissionError("PR #9 must be open and unmerged.")
    head = pr.get("head", {})
    if head.get("ref") != BRANCH or head.get("repo", {}).get("full_name") != REPO:
        raise AdmissionError("PR #9 must use the approved same-repository source branch.")
    sha = head.get("sha", "")
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise AdmissionError("Could not establish a canonical 40-character PR HEAD.")
    if expected is not None and sha != expected:
        raise AdmissionError("PR HEAD changed; re-review the exact new SHA before triggering.")
    return sha


def configure(reviewer: str) -> None:
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]{0,38}", reviewer):
        raise AdmissionError("Provide an explicit independent GitHub reviewer login.")
    current = api_json("user")
    reviewer_user = api_json(f"users/{reviewer}")
    if current.get("id") == reviewer_user.get("id"):
        raise AdmissionError("A self-reviewer cannot approve an independently protected SQL gate.")
    ensure_pr_head()
    payload = {
        "wait_timer": 0,
        "prevent_self_review": True,
        "reviewers": [{"type": "User", "id": reviewer_user["id"]}],
        "deployment_branch_policy": {
            "protected_branches": False,
            "custom_branch_policies": True,
        },
    }
    gh("api", "--method", "PUT", ENV_API, data=payload)
    policy_path = f"{ENV_API}/deployment-branch-policies"
    existing = api_json(f"{policy_path}?per_page=100")
    patterns = {p.get("name") for p in existing.get("branch_policies", [])}
    if patterns and patterns != {BRANCH}:
        raise AdmissionError("Other deployment branch policies exist; inspect them manually. No label added.")
    if BRANCH not in patterns:
        gh("api", "--method", "POST", policy_path, data={"name": BRANCH, "type": "branch"})
    labels = api_json(f"repos/{REPO}/labels?per_page=100")
    if LABEL not in {item.get("name") for item in labels}:
        gh("label", "create", LABEL, "--repo", REPO, "--color", "B60205",
           "--description", "Explicitly approved isolated SQL13 live acceptance trigger")
    verify(require_secrets=False)
    print("Protected environment and exact branch/reviewer policy: VERIFIED")
    print("Set environment-scoped secrets/variables privately, then run verify and trigger.")


def verify(require_secrets: bool = True) -> str:
    sha = ensure_pr_head()
    environment = api_json(ENV_API)
    policy = environment.get("deployment_branch_policy") or {}
    if policy.get("protected_branches") is not False or policy.get("custom_branch_policies") is not True:
        raise AdmissionError("Environment must enforce a custom deployment branch restriction.")
    rules = [r for r in environment.get("protection_rules", [])
             if r.get("type") == "required_reviewers"]
    if len(rules) != 1 or not rules[0].get("reviewers"):
        raise AdmissionError("Missing REQUIRED reviewer protection; cannot trigger the SQL gate.")
    if not rules[0].get("prevent_self_review", environment.get("prevent_self_review", False)):
        raise AdmissionError("Prevent self-review must be on.")
    policies = api_json(f"{ENV_API}/deployment-branch-policies?per_page=100")
    names = {p.get("name") for p in policies.get("branch_policies", [])}
    if names != {BRANCH}:
        raise AdmissionError("Environment must allow only the approved PR branch.")
    if require_secrets:
        present_secrets = {s.get("name") for s in
                           api_json(f"{ENV_API}/secrets?per_page=100").get("secrets", [])}
        present_vars = {v.get("name") for v in
                        api_json(f"{ENV_API}/variables?per_page=100").get("variables", [])}
        missing_secrets, missing_vars = SECRETS - present_secrets, VARIABLES - present_vars
        if missing_secrets or missing_vars:
            # List missing *names* only, never secret values.
            raise AdmissionError("Missing environment secrets: " + ", ".join(sorted(missing_secrets)) +
                                 "; environment variables: " + ", ".join(sorted(missing_vars)))
    print("Required reviewers, prevent self-review and exact branch policy: VERIFIED")
    print("Environment secret/variable names:", "VERIFIED" if require_secrets else "NOT CHECKED")
    print("PR #9 exact HEAD:", sha)
    print("Operator must separately verify SQL is isolated/nonproduction, schema13 and TLS/grants.")
    return sha


def trigger(expected_sha: str, confirmation: str) -> None:
    if confirmation != "approved-isolated-sql13":
        raise AdmissionError("Explicitly acknowledge approved isolated nonproduction SQL13.")
    if not re.fullmatch(r"[0-9a-f]{40}", expected_sha):
        raise AdmissionError("Pass the full reviewed 40-character --expected-sha.")
    sha = verify()
    ensure_pr_head(expected_sha)
    if sha != expected_sha:
        raise AdmissionError("HEAD changed after preflight; refusing to trigger.")
    existing_labels = {x.get("name") for x in
                       api_json(f"repos/{REPO}/issues/{PR}/labels?per_page=100")}
    if LABEL in existing_labels:
        raise AdmissionError("Label already present; remove and reapply only after revalidation.")
    gh("pr", "edit", str(PR), "--repo", REPO, "--add-label", LABEL)
    print("PR label applied; independent protected Environment review is still required.")
    print("NOT VERIFIED until live GitHub Actions has 4/4 executed/passed with zero skipped.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    configure_cmd = commands.add_parser("configure")
    configure_cmd.add_argument("--reviewer", required=True)
    commands.add_parser("verify")
    trigger_cmd = commands.add_parser("trigger")
    trigger_cmd.add_argument("--expected-sha", required=True)
    trigger_cmd.add_argument("--confirm", required=True)
    args = parser.parse_args()
    try:
        if args.action == "configure":
            configure(args.reviewer)
        elif args.action == "verify":
            verify()
        else:
            trigger(args.expected_sha, args.confirm)
    except (AdmissionError, KeyError, ValueError, json.JSONDecodeError) as err:
        print("BLOCKED:", err, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
