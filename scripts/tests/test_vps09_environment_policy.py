import unittest
from pathlib import Path
from unittest.mock import patch

from scripts.configure_vps09_gates import API, REPO, GateBlocked, environment_specs, protection_payload, verify


class Vps09EnvironmentPolicyTests(unittest.TestCase):
    def test_all_three_protected_environments_have_exact_runner_and_settings(self):
        specs = environment_specs()

        self.assertEqual({
            "vps08-sql13-nonproduction",
            "vps09-fullstack-nonproduction",
            "vps09-release-review",
        }, set(specs))
        self.assertEqual(("self-hosted", "linux", "vps08-staging"),
                         specs["vps08-sql13-nonproduction"]["runnerLabels"])
        self.assertEqual(("self-hosted", "windows", "vps09-attended"),
                         specs["vps09-fullstack-nonproduction"]["runnerLabels"])
        self.assertEqual(("self-hosted", "linux", "vps09-reviewer"),
                         specs["vps09-release-review"]["runnerLabels"])
        self.assertIn("VPS09_ROUTE_PLAN_SHA256",
                      specs["vps09-fullstack-nonproduction"]["requiredVariables"])
        self.assertIn("VPS09_ORACLE_PLAN_SHA256",
                      specs["vps09-fullstack-nonproduction"]["requiredVariables"])
        self.assertIn("VPS09_FIRST_PASS_RUNNER_SHA256",
                      specs["vps09-fullstack-nonproduction"]["requiredVariables"])
        self.assertIn("VPS09_SECOND_PASS_RUNNER_SHA256",
                      specs["vps09-release-review"]["requiredVariables"])
        for spec in specs.values():
            self.assertEqual("main", spec["branch"])
            self.assertFalse(spec["preventSelfReview"])
            self.assertTrue(spec["adminBypassDisabled"])
            self.assertTrue(spec["requiredVariables"])
            self.assertEqual("OWN-VPS-04", spec["decisionId"])
            self.assertIn("VPS09_CLOSEOUT_CANDIDATE_SHA", spec["requiredVariables"])
        self.assertIn("VPS09_FIRST_PASS_RUNNER_SHA256",
                      specs["vps09-release-review"]["requiredVariables"])
        self.assertEqual("VPS08_OWNER", specs["vps08-sql13-nonproduction"]["ownerVariable"])
        self.assertEqual("VPS09_OWNER", specs["vps09-fullstack-nonproduction"]["ownerVariable"])
        self.assertEqual("VPS09_OWNER", specs["vps09-release-review"]["ownerVariable"])

    def test_protection_payload_has_no_fake_reviewer_and_exact_main_policy(self):
        payload = protection_payload()

        self.assertFalse(payload["prevent_self_review"])
        self.assertEqual([], payload["reviewers"])
        self.assertEqual({"protected_branches": False, "custom_branch_policies": True},
                         payload["deployment_branch_policy"])


    def test_every_environment_accepts_only_its_declared_owner_variable(self):
        candidate = "a" * 40
        for environment, spec in environment_specs().items():
            with self.subTest(environment=environment):
                def mock_get(path):
                    if path == f"{API}/{environment}":
                        return {
                            "deployment_branch_policy": {
                                "protected_branches": False, "custom_branch_policies": True,
                            },
                            "protection_rules": [{"type": "branch_policy"}],
                        }
                    if path == f"{API}/{environment}/deployment-branch-policies?per_page=100":
                        return {"branch_policies": [{"name": "main", "type": "branch"}]}
                    if path == f"{API}/{environment}/secrets?per_page=100":
                        return {"secrets": [{"name": name} for name in spec["requiredSecrets"]]}
                    if path == f"{API}/{environment}/variables?per_page=100":
                        values = {name: "placeholder" for name in spec["requiredVariables"]}
                        values["VPS09_CLOSEOUT_CANDIDATE_SHA"] = candidate
                        values[spec["ownerVariable"]] = "chung6a8m"
                        return {"variables": [{"name": name, "value": value}
                                              for name, value in values.items()]}
                    if path == f"repos/{REPO}/branches/main":
                        return {"commit": {"sha": candidate}}
                    if path == f"repos/{REPO}/actions/runners?per_page=100":
                        return {"runners": [{
                            "status": "online",
                            "labels": [{"name": label} for label in spec["runnerLabels"]],
                        }]}
                    raise AssertionError("Unexpected mocked GitHub endpoint: " + path)

                with patch("scripts.configure_vps09_gates.get", side_effect=mock_get):
                    verify(environment, candidate,
                           f"confirmed-disabled:OWN-VPS-04:{environment}:{candidate}")

    def test_vps08_cannot_substitute_vps09_owner_variable(self):
        candidate = "a" * 40
        spec = environment_specs()["vps08-sql13-nonproduction"]
        self.assertNotIn("VPS09_OWNER", spec["requiredVariables"])
        self.assertIn("VPS08_OWNER", spec["requiredVariables"])

    def test_every_protected_job_binds_owner_dispatch_and_frozen_candidate(self):
        root = Path(__file__).resolve().parents[2]
        vps09 = (root / ".github/workflows/vps-09-evidence.yml").read_text(encoding="utf-8")
        vps08 = (root / ".github/workflows/vps-08-linux-deploy.yml").read_text(encoding="utf-8")

        self.assertEqual(4, vps09.count("github.actor == 'chung6a8m'"))
        self.assertEqual(4, vps09.count("github.triggering_actor == 'chung6a8m'"))
        self.assertGreaterEqual(vps09.count("VPS09_CLOSEOUT_CANDIDATE_SHA"), 4)
        self.assertIn("--expected-first-pass-runner-sha256", vps09)
        self.assertIn("github.actor == 'chung6a8m'", vps08)
        self.assertIn("github.triggering_actor == 'chung6a8m'", vps08)
        self.assertIn("VPS09_CLOSEOUT_CANDIDATE_SHA", vps08)


if __name__ == "__main__":
    unittest.main()
