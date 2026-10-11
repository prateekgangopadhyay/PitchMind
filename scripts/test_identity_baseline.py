"""Static guards for the identity configuration currently present in this repo.

These tests validate source declarations only. They do not prove Azure RBAC,
federation, token acquisition, database grants, or denied operations in DEV.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


class IdentityBaselineTests(unittest.TestCase):
    def test_offline_demo_declares_registry_scoped_acrpull(self):
        demo = (ROOT / "infra/modules/demo-workload.bicep").read_text()
        registry = (ROOT / "infra/modules/registry.bicep").read_text()

        self.assertIn("Microsoft.ManagedIdentity/userAssignedIdentities", demo)
        self.assertIn("resource pull 'Microsoft.Authorization/roleAssignments", demo)
        self.assertIn("scope: registry", demo)
        self.assertIn("guid(registry.id, identity.id, 'AcrPull')", demo)
        self.assertIn("7f951dda-4ed3-4680-a7ca-43fe172d538d", demo)
        self.assertIn("userAssignedIdentities: { '${identity.id}': {} }", demo)
        self.assertIn("registries: [{ server: registry.properties.loginServer, identity: identity.id }]", demo)
        self.assertIn("adminUserEnabled: false", registry)

        # Fail closed if this demo template starts declaring broader role scopes.
        self.assertNotRegex(demo, r"(?m)^\s*scope:\s*(?:resourceGroup\(\)|subscription\(\))")

    def test_azure_pipeline_yaml_has_no_secret_shaped_parameters(self):
        # Structural scan of this YAML file only. This does not establish that
        # the Azure DevOps service connection exists or uses working federation.
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        secret_parameter = re.compile(
            r"(?im)^\s*(?:-\s*)?(?:client[_-]?secret|service[_-]?principal[_-]?secret|password)\s*:\s*"
        )
        self.assertIsNone(secret_parameter.search(pipeline))

    def test_release_pipeline_routes_separate_connection_parameters_to_push_and_deploy(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        self.assertIn("name: acrPushServiceConnection", pipeline)
        self.assertIn("name: devDeploymentServiceConnection", pipeline)
        self.assertIn("acrPushServiceConnection: ${{ parameters.acrPushServiceConnection }}", pipeline)
        self.assertIn("devDeploymentServiceConnection: ${{ parameters.devDeploymentServiceConnection }}", pipeline)
        self.assertIn("azureSubscription: ${{ parameters.acrPushServiceConnection }}", build_template)
        self.assertIn("azureSubscription: ${{ parameters.devDeploymentServiceConnection }}", deploy_template)
        self.assertNotIn("azureServiceConnection", pipeline + build_template + deploy_template)
        self.assertIn("python3 scripts/validate_release_identity_inputs.py", pipeline)
        validation_job = pipeline.index("- job: ValidateReleaseIdentityInputs")
        approval_job = pipeline.index("- job: AwaitExplicitOwnerApproval")
        approval_job_end = pipeline.index("\n  - stage: BuildRelease", approval_job)
        approval_job_text = pipeline[approval_job:approval_job_end]
        self.assertLess(validation_job, approval_job)
        self.assertIn("vmImage: ubuntu-latest", pipeline[validation_job:approval_job])
        self.assertIn("pool: server", approval_job_text)
        self.assertIn("dependsOn: ValidateReleaseIdentityInputs", approval_job_text)
        self.assertIn("- task: ManualValidation@0", approval_job_text)
        self.assertIn("users with Queue builds permission", pipeline)

    def test_key_vault_module_declares_rbac_without_local_grants(self):
        # This assertion is intentionally limited to the module itself; it is
        # not an Azure authorization or repository-wide RBAC proof.
        vault = (ROOT / "infra/modules/key-vault.bicep").read_text()
        self.assertIn("enableRbacAuthorization: true", vault)
        self.assertIn("publicNetworkAccess: 'Disabled'", vault)
        self.assertIn("enablePurgeProtection: true", vault)
        self.assertNotIn("Microsoft.Authorization/roleAssignments", vault)


if __name__ == "__main__":
    unittest.main()
