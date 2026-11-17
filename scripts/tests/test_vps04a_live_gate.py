"""Offline regressions for PR #9's protected acceptance Environment admission."""
from __future__ import annotations

import importlib.util
from pathlib import Path
from unittest import TestCase, main
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "vps04a_live_gate", ROOT / "scripts/configure-vps04a-live-gate.py")
assert spec is not None and spec.loader is not None
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
SHA = "a" * 40


def mock_api(names: list[str]):
    policies = f"{gate.ENV_API}/deployment-branch-policies?per_page=100"
    data = {
        f"repos/{gate.REPO}/pulls/{gate.PR}": {
            "state": "open", "merged_at": None,
            "head": {"ref": gate.BRANCH, "repo": {"full_name": gate.REPO}, "sha": SHA},
        },
        gate.ENV_API: {
            "can_admins_bypass": False,
            "deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True},
            "protection_rules": [{"type": "required_reviewers", "prevent_self_review": True,
                                  "reviewers": [{"type": "User", "reviewer": {"id": 22}}]}],
        },
        policies: {"total_count": len(names),
                   "branch_policies": [{"name": name, "id": 123 + i} for i, name in enumerate(names)]},
        "user": {"id": 11},
        "users/independent": {"id": 22},
        f"repos/{gate.REPO}/labels?per_page=100": [{"name": gate.LABEL}],
    }
    return lambda path: data[path]


class ProtectedLiveGateTests(TestCase):
    def test_merge_ref_not_source_branch(self):
        self.assertEqual(gate.DEPLOY_REF, "refs/pull/9/merge")
        self.assertNotEqual(gate.DEPLOY_REF, gate.BRANCH)

    def test_verify_accepts_only_exact_pr_ref(self):
        with patch.object(gate, "api_json", side_effect=mock_api([gate.DEPLOY_REF])):
            self.assertEqual(gate.verify(require_secrets=False), SHA)

    def test_verify_rejects_source_branch_or_other_pr(self):
        for names in ([gate.BRANCH], [gate.DEPLOY_REF, "refs/pull/10/merge"]):
            with self.subTest(names=names):
                with patch.object(gate, "api_json", side_effect=mock_api(names)):
                    with self.assertRaises(gate.AdmissionError):
                        gate.verify(require_secrets=False)

    def test_configure_migrates_only_original_source_branch_rule(self):
        calls = []
        def fake_gh(*args, data=None):
            calls.append((args, data))
            return ""
        with patch.object(gate, "api_json", side_effect=mock_api([gate.BRANCH])), \
             patch.object(gate, "gh", side_effect=fake_gh):
            gate.configure("independent")
        policy_path = f"{gate.ENV_API}/deployment-branch-policies"
        self.assertIn((("api", "--method", "DELETE", f"{policy_path}/123"), None), calls)
        add = (("api", "--method", "POST", policy_path),
               {"name": gate.DEPLOY_REF, "type": "branch"})
        delete = (("api", "--method", "DELETE", f"{policy_path}/123"), None)
        self.assertIn(add, calls)
        self.assertLess(calls.index(add), calls.index(delete))

    def test_migration_post_failure_preserves_existing_policy(self):
        calls = []
        policy_path = f"{gate.ENV_API}/deployment-branch-policies"
        def fake_gh(*args, data=None):
            calls.append((args, data))
            if args == ("api", "--method", "POST", policy_path):
                raise gate.AdmissionError("simulated GitHub policy write failure")
            return ""
        with patch.object(gate, "api_json", side_effect=mock_api([gate.BRANCH])), \
             patch.object(gate, "gh", side_effect=fake_gh):
            with self.assertRaises(gate.AdmissionError):
                gate.configure("independent")
        self.assertFalse(any("DELETE" in args for args, _ in calls))

    def test_configure_refuses_unrelated_policy(self):
        calls = []
        with patch.object(gate, "api_json", side_effect=mock_api(["refs/pull/10/merge"])), \
             patch.object(gate, "gh", side_effect=lambda *args, data=None: calls.append((args, data))):
            with self.assertRaises(gate.AdmissionError):
                gate.configure("independent")
        self.assertFalse(any("DELETE" in args for args, _ in calls))

    def test_live_workflow_pins_ref_pr_identity_and_reviewed_head(self):
        caller = (ROOT / ".github/workflows/vps-04a-linux.yml").read_text(encoding="utf-8")
        live = (ROOT / ".github/workflows/vps-04a-live-acceptance.yml").read_text(encoding="utf-8")
        for source in (caller, live):
            self.assertIn("github.ref == 'refs/pull/9/merge'", source)
            self.assertIn("github.event.pull_request.number == 9", source)
            self.assertIn("github.event.pull_request.base.ref == 'main'", source)
        self.assertIn("ref: ${{ github.event.pull_request.head.sha }}", live)
        self.assertIn("persist-credentials: false", live)
        self.assertIn("gh api", live)
        self.assertIn("REVIEWED_HEAD", live)
        self.assertNotIn("workflow_dispatch:", live)


if __name__ == "__main__":
    main()
