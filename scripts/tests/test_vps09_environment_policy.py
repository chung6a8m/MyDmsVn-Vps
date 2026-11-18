import unittest

from scripts.configure_vps09_gates import GateBlocked, environment_specs, protection_payload


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
        for spec in specs.values():
            self.assertEqual("main", spec["branch"])
            self.assertTrue(spec["preventSelfReview"])
            self.assertTrue(spec["adminBypassDisabled"])
            self.assertTrue(spec["requiredVariables"])

    def test_same_operator_and_reviewer_is_refused(self):
        with self.assertRaisesRegex(GateBlocked, "SameAccountApprovalForbidden"):
            protection_payload(operator_id=42, reviewer_id=42)

    def test_protection_payload_requires_one_reviewer_and_exact_main_policy(self):
        payload = protection_payload(operator_id=1, reviewer_id=2)

        self.assertTrue(payload["prevent_self_review"])
        self.assertEqual([{"type": "User", "id": 2}], payload["reviewers"])
        self.assertEqual({"protected_branches": False, "custom_branch_policies": True},
                         payload["deployment_branch_policy"])


if __name__ == "__main__":
    unittest.main()
