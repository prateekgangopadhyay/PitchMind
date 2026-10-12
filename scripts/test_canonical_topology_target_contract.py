"""Static contract checks for typed existing-service targets in the Bicep candidate.

These tests guard source-level wiring only. They do not parse or compile Bicep,
resolve Azure resource scopes, verify target policy, or prove deployment safety.
"""

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]
TEMPLATE = ROOT / "infra" / "canonical-dev-topology.bicep"
EXAMPLE = ROOT / "infra" / "examples" / "canonical-dev-topology.example.bicepparam"


class CanonicalTopologyTargetContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.template = TEMPLATE.read_text(encoding="utf-8")
        cls.example = EXAMPLE.read_text(encoding="utf-8")

    def test_candidate_stays_disabled_by_default_and_dev_only(self):
        self.assertRegex(self.template, r"(?m)^param deployCanonicalTopology bool = false$")
        self.assertRegex(self.template, r"(?m)^@allowed\(\['dev'\]\)$")
        self.assertRegex(self.template, r"(?m)^param environment string = 'dev'$")

    def test_every_deployable_resource_is_behind_the_opt_in_guard(self):
        resources = re.findall(
            r"(?m)^resource[ \t]+(\w+)[ \t]+'[^']+'([ \t]+existing)?[ \t]*=[ \t]*(.*)$",
            self.template,
        )
        deployable = [
            (name, declaration)
            for name, existing, declaration in resources
            if existing.strip() != "existing"
        ]
        self.assertGreater(len(deployable), 0)
        for name, declaration in deployable:
            with self.subTest(resource=name):
                self.assertEqual(declaration, "if (deployCanonicalTopology) {")

    def test_provisioning_modules_cannot_bypass_the_opt_in_guard(self):
        def unguarded_modules(source):
            declarations = re.findall(
                r"(?m)^[ \t]*module[ \t]+(\w+)[ \t]+'[^']+'[ \t]*=[ \t]*(?:\r?\n[ \t]*)?([^\r\n]+)",
                source,
            )
            return [
                name
                for name, expression in declarations
                if expression.strip() != "if (deployCanonicalTopology) {"
            ]

        self.assertEqual([], unguarded_modules(self.template))

        unguarded_fixture = self.template + (
            "\nmodule hiddenWorkload './workload.bicep' = {\n"
            "  name: 'workload'\n"
            "}\n"
        )
        self.assertEqual(["hiddenWorkload"], unguarded_modules(unguarded_fixture))

        guarded_fixture = self.template + (
            "\nmodule optionalWorkload './workload.bicep' = if (deployCanonicalTopology) {\n"
            "  name: 'workload'\n"
            "}\n"
        )
        self.assertEqual([], unguarded_modules(guarded_fixture))

    def test_disabled_outputs_do_not_dereference_undeployed_resources(self):
        guarded_outputs = (
            "postgresServerId",
            "postgresServerName",
            "postgresFqdn",
            "containerAppsEnvironmentId",
            "storagePrivateEndpointId",
            "keyVaultPrivateEndpointId",
            "eventHubsPrivateEndpointId",
        )
        for output_name in guarded_outputs:
            with self.subTest(output=output_name):
                self.assertRegex(
                    self.template,
                    rf"(?m)^output {output_name} \w+ = deployCanonicalTopology \? .+ : ''$",
                )

    def test_targets_are_fixed_type_existing_resources_at_explicit_scopes(self):
        targets = {
            "storageAccount": (
                "Microsoft.Storage/storageAccounts@2023-05-01",
                "storageAccountName",
                "storageSubscriptionId",
                "storageResourceGroupName",
            ),
            "keyVault": (
                "Microsoft.KeyVault/vaults@2023-07-01",
                "keyVaultName",
                "keyVaultSubscriptionId",
                "keyVaultResourceGroupName",
            ),
            "eventHubsNamespace": (
                "Microsoft.EventHub/namespaces@2024-01-01",
                "eventHubsNamespaceName",
                "eventHubsSubscriptionId",
                "eventHubsResourceGroupName",
            ),
        }
        for symbol, (resource_type, name_param, subscription_param, group_param) in targets.items():
            with self.subTest(target=symbol):
                self.assertRegex(
                    self.template,
                    rf"(?s)resource {symbol} '{re.escape(resource_type)}' existing = \{{.*?"
                    rf"name: {name_param}.*?scope: resourceGroup\({subscription_param}, {group_param}\).*?\}}",
                )

    def test_private_endpoints_use_the_fixed_type_resource_ids(self):
        endpoints = {
            "storagePrivateEndpoint": ("storageAccount.id", "['blob']"),
            "keyVaultPrivateEndpoint": ("keyVault.id", "['vault']"),
            "eventHubsPrivateEndpoint": ("eventHubsNamespace.id", "['namespace']"),
        }
        for symbol, (target_id, group_ids) in endpoints.items():
            with self.subTest(endpoint=symbol):
                block = re.search(
                    rf"(?s)resource {symbol} .*?= if \(deployCanonicalTopology\) \{{(.*?)\n\}}",
                    self.template,
                )
                self.assertIsNotNone(block)
                self.assertIn(f"privateLinkServiceId: {target_id}", block.group(1))
                self.assertIn(f"groupIds: {group_ids}", block.group(1))
        self.assertNotRegex(self.template, r"(?m)^param \w+ResourceId string$")

    def test_example_supplies_all_scope_and_name_inputs_but_keeps_flag_off(self):
        required = (
            "storageSubscriptionId",
            "storageResourceGroupName",
            "storageAccountName",
            "keyVaultSubscriptionId",
            "keyVaultResourceGroupName",
            "keyVaultName",
            "eventHubsSubscriptionId",
            "eventHubsResourceGroupName",
            "eventHubsNamespaceName",
        )
        for name in required:
            with self.subTest(parameter=name):
                self.assertRegex(self.example, rf"(?m)^param {name} = .+$")
        self.assertRegex(self.example, r"(?m)^param deployCanonicalTopology = false$")
        self.assertIn("Non-deployable placeholders", self.example)

    def test_example_covers_every_required_template_parameter(self):
        required = set(
            re.findall(r"(?m)^param\s+(\w+)\s+\w+(?:\[\])?\s*$", self.template)
        )
        provided = set(re.findall(r"(?m)^param\s+(\w+)\s*=", self.example))
        self.assertEqual(required - provided, set())


if __name__ == "__main__":
    unittest.main()
