#!/usr/bin/env python3
"""Configure or verify the three OWN-VPS-04 VPS-08/VPS-09 Environments.

The helper validates protection, names, exact main and dedicated runner labels.
It cannot validate secret values, physical SQL/VPS isolation, GUI attendance or
Owner approval.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
from typing import Any


REPO = "chung6a8m/MyDmsVn-Vps"
API = f"repos/{REPO}/environments"


class GateBlocked(ValueError):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise GateBlocked(code)


def environment_specs() -> dict[str, dict[str, Any]]:
    return {
        "vps08-sql13-nonproduction": {
            "branch": "main",
            "preventSelfReview": False,
            "adminBypassDisabled": True,
            "decisionId": "OWN-VPS-04",
            "runnerLabels": ("self-hosted", "linux", "vps08-staging"),
            "ownerVariable": "VPS08_OWNER",
            "requiredSecrets": {"VPS08_STAGING_HOST_ATTESTATION"},
            "requiredVariables": {
                "VPS08_STAGING_PROBE_URL", "VPS08_STAGING_MACHINE_SHA256",
                "VPS08_RECEIPT_STATE_PATH", "VPS08_OWNER",
                "VPS09_CLOSEOUT_CANDIDATE_SHA",
            },
        },
        "vps09-fullstack-nonproduction": {
            "branch": "main",
            "preventSelfReview": False,
            "adminBypassDisabled": True,
            "decisionId": "OWN-VPS-04",
            "runnerLabels": ("self-hosted", "windows", "vps09-attended"),
            "ownerVariable": "VPS09_OWNER",
            "requiredSecrets": {
                "VPS09_ISOLATED_SQL_ADMISSION", "VPS09_READONLY_SQL_ORACLE",
                "VPS09_JWT_AUDIENCE", "VPS09_WRITER_USERNAME", "VPS09_WRITER_PASSWORD",
                "VPS09_DENIED_USERNAME", "VPS09_DENIED_PASSWORD",
                "VPS09_OPERATOR_USERNAME", "VPS09_OPERATOR_PASSWORD",
                "VPS09_CLEANUP_ATTESTATION",
            },
            "requiredVariables": {
                "VPS09_PUBLIC_ORIGIN", "VPS09_CUSTOMER_ID", "VPS09_WAREHOUSE_ID",
                "VPS09_EMPLOYEE_ID", "VPS09_PRODUCT_ID", "VPS09_ROUTE_PLAN_PATH",
                "VPS09_ORACLE_PLAN_PATH", "VPS09_ROUTE_PLAN_SHA256",
                "VPS09_ORACLE_PLAN_SHA256", "VPS09_EVIDENCE_ROOT",
                "VPS09_GUI_CHECKLIST_PATH", "VPS09_LINUX_RECEIPT_PATH",
                "VPS09_FIRST_PASS_RUNNER_SHA256", "VPS09_OWNER",
                "VPS09_CLOSEOUT_CANDIDATE_SHA",
            },
        },
        "vps09-release-review": {
            "branch": "main",
            "preventSelfReview": False,
            "adminBypassDisabled": True,
            "decisionId": "OWN-VPS-04",
            "runnerLabels": ("self-hosted", "linux", "vps09-reviewer"),
            "ownerVariable": "VPS09_OWNER",
            "requiredSecrets": set(),
            "requiredVariables": {
                "VPS09_REVIEW_PACKET_PATH", "VPS09_OWNER_PUBKEY_PATH",
                "VPS09_OWNER_PUBKEY_SHA256", "VPS09_SECOND_PASS_RUNNER_SHA256",
                "VPS09_FIRST_PASS_RUNNER_SHA256", "VPS09_CLOSEOUT_CANDIDATE_SHA",
                "VPS09_OWNER",
            },
        },
    }


def protection_payload() -> dict[str, Any]:
    return {
        "wait_timer": 0,
        "prevent_self_review": False,
        "reviewers": [],
        "deployment_branch_policy": {
            "protected_branches": False,
            "custom_branch_policies": True,
        },
    }


def gh(*arguments: str, data: dict[str, Any] | None = None) -> Any:
    command = ["gh", *arguments]
    if data is not None:
        command.extend(["--input", "-"])
    result = subprocess.run(command, input=json.dumps(data) if data is not None else None,
                            text=True, capture_output=True)
    require(result.returncode == 0, "GitHubApiFailed")
    return json.loads(result.stdout) if result.stdout.strip() else None


def get(path: str) -> Any:
    return gh("api", path)


def configure(environment: str) -> None:
    specs = environment_specs()
    require(environment in specs, "EnvironmentUnknown")
    payload = protection_payload()
    gh("api", "--method", "PUT", f"{API}/{environment}", data=payload)
    policies = get(f"{API}/{environment}/deployment-branch-policies?per_page=100")
    rows = policies.get("branch_policies", [])
    require(all(item.get("name") == "main" and item.get("type") == "branch" for item in rows),
            "ExistingBranchPolicyUnexpected")
    if not rows:
        gh("api", "--method", "POST", f"{API}/{environment}/deployment-branch-policies",
           data={"name": "main", "type": "branch"})
    print(f"VPS09_ENV_CONFIGURED: {environment}; governance=OWN-VPS-04; "
          "NO_INDEPENDENT_HUMAN_REVIEW; disable admin bypass manually before verify")


def verify(environment: str, expected_sha: str, admin_bypass_attestation: str) -> None:
    spec = environment_specs().get(environment)
    require(spec is not None, "EnvironmentUnknown")
    require(re.fullmatch(r"[0-9a-f]{40}", expected_sha) is not None, "ExpectedShaInvalid")
    value = get(f"{API}/{environment}")
    expected_attestation = f"confirmed-disabled:OWN-VPS-04:{environment}:{expected_sha}"
    require(admin_bypass_attestation == expected_attestation,
            "AdminBypassAttestationMissing")
    policy = value.get("deployment_branch_policy") or {}
    require(policy == {"protected_branches": False, "custom_branch_policies": True},
            "DeploymentPolicyInvalid")
    require(isinstance(value.get("protection_rules"), list),
            "ProtectionRulesUnavailable")
    rules = [item for item in value["protection_rules"]
             if item.get("type") == "required_reviewers"]
    require(not rules, "UnexpectedHumanReviewerRule")
    policies = get(f"{API}/{environment}/deployment-branch-policies?per_page=100")
    require({(item.get("name"), item.get("type"))
             for item in policies.get("branch_policies", [])} == {("main", "branch")},
            "ExactMainPolicyRequired")
    secrets = get(f"{API}/{environment}/secrets?per_page=100")
    variables = get(f"{API}/{environment}/variables?per_page=100")
    secret_names = {item.get("name") for item in secrets.get("secrets", [])}
    variable_names = {item.get("name") for item in variables.get("variables", [])}
    require(spec["requiredSecrets"] <= secret_names, "RequiredSecretNameMissing")
    require(spec["requiredVariables"] <= variable_names, "RequiredVariableNameMissing")
    variable_values = {item.get("name"): item.get("value")
                       for item in variables.get("variables", [])}
    require(variable_values.get("VPS09_CLOSEOUT_CANDIDATE_SHA") == expected_sha,
            "CandidateShaNotPinned")
    require(variable_values.get(spec["ownerVariable"]) == "chung6a8m", "OwnerNotPinned")
    branch = get(f"repos/{REPO}/branches/main")
    require(branch["commit"]["sha"] == expected_sha, "ReviewedMainChanged")
    runners = get(f"repos/{REPO}/actions/runners?per_page=100")
    required_labels = set(spec["runnerLabels"])
    admitted = [runner for runner in runners.get("runners", [])
                if runner.get("status") == "online"
                and required_labels <= {label.get("name") for label in runner.get("labels", [])}]
    require(len(admitted) == 1, "ExactlyOneDedicatedOnlineRunnerRequired")
    print(f"VPS09_ENV_VERIFY_PASS: {environment}; main_sha={expected_sha}; "
          "governance=OWN-VPS-04; candidate, names and runner admitted; "
          "admin-bypass attestation recorded")
    print("NO_INDEPENDENT_HUMAN_REVIEW; NOT VERIFIED: secret values, SQL/VPS isolation, "
          "GUI/reboot evidence, Owner release")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    configure_command = commands.add_parser("configure")
    configure_command.add_argument("--environment", required=True,
                                   choices=tuple(environment_specs()))
    verify_command = commands.add_parser("verify")
    verify_command.add_argument("--environment", required=True,
                                choices=tuple(environment_specs()))
    verify_command.add_argument("--expected-sha", required=True)
    verify_command.add_argument("--admin-bypass-attestation", required=True,
                                help="Exact confirmed-disabled:OWN-VPS-04:ENVIRONMENT:SHA value recorded after checking GitHub Settings")
    args = parser.parse_args()
    try:
        if args.command == "configure":
            configure(args.environment)
        else:
            verify(args.environment, args.expected_sha, args.admin_bypass_attestation)
        return 0
    except (GateBlocked, OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as error:
        print("VPS09_ENV_BLOCKED: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
