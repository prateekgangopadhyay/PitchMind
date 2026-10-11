"""Static guards for the identity configuration currently present in this repo.

These tests validate source declarations only. They do not prove Azure RBAC,
federation, token acquisition, database grants, or denied operations in DEV.
"""
from pathlib import Path
import re
import unittest

from scripts.release_pipeline_policy import PipelinePolicyError, validate_pipeline_policy

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

    def test_release_pipeline_identity_and_delivery_policy(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()
        validate_pipeline_policy(pipeline, build_template, deploy_template)

    def test_policy_mutations_reject_default_on_and_pull_request_delivery(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        default_on = pipeline.replace("default: false", "default: true", 1)
        with self.subTest(mutation="delivery default enabled"), self.assertRaises(PipelinePolicyError):
            validate_pipeline_policy(default_on, build_template, deploy_template)

        pr_eligible = pipeline.replace(
            "ne(variables['Build.Reason'], 'PullRequest')",
            "eq(variables['Build.Reason'], 'PullRequest')",
            1,
        )
        with self.subTest(mutation="owner approval stage PR condition"), self.assertRaises(PipelinePolicyError):
            validate_pipeline_policy(pr_eligible, build_template, deploy_template)

        pr_eligible_build = pipeline.replace(
            "ne(variables['Build.Reason'], 'PullRequest')",
            "eq(variables['Build.Reason'], 'PullRequest')",
            2,
        )
        with self.subTest(mutation="build stage PR condition"), self.assertRaises(PipelinePolicyError):
            validate_pipeline_policy(pr_eligible_build, build_template, deploy_template)

        unguarded_deploy = pipeline.replace(
            "  - stage: DeployDev\n    dependsOn: OwnerApprovalOfReleaseDigest",
            "  - stage: DeployDev\n    dependsOn: Validate",
            1,
        )
        with self.subTest(mutation="DEV deployment bypasses guarded release chain"), self.assertRaises(PipelinePolicyError):
            validate_pipeline_policy(unguarded_deploy, build_template, deploy_template)

    def test_policy_mutations_reject_comment_only_release_guards(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        commented_opt_in = pipeline.replace(
            "- ${{ if eq(parameters.enableDevDelivery, true) }}:",
            "# - ${{ if eq(parameters.enableDevDelivery, true) }}:",
            1,
        )
        with self.subTest(mutation="opt-in expression exists only as a comment"), self.assertRaises(PipelinePolicyError):
            validate_pipeline_policy(commented_opt_in, build_template, deploy_template)

        commented_conditions = pipeline.replace(
            "    condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'), ne(variables['Build.Reason'], 'PullRequest'))",
            "    # condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'), ne(variables['Build.Reason'], 'PullRequest'))",
            2,
        )
        with self.subTest(mutation="main and PR conditions exist only as comments"), self.assertRaises(PipelinePolicyError):
            validate_pipeline_policy(commented_conditions, build_template, deploy_template)

    def test_policy_mutations_reject_swapped_or_reused_connection_wiring(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        swapped_build = build_template.replace(
            "azureSubscription: ${{ parameters.acrPushServiceConnection }}",
            "azureSubscription: ${{ parameters.devDeploymentServiceConnection }}",
            1,
        )
        with self.subTest(mutation="build uses deployment connection"), self.assertRaises(PipelinePolicyError):
            validate_pipeline_policy(pipeline, swapped_build, deploy_template)

        swapped_deploy = deploy_template.replace(
            "azureSubscription: ${{ parameters.devDeploymentServiceConnection }}",
            "azureSubscription: ${{ parameters.acrPushServiceConnection }}",
            1,
        )
        with self.subTest(mutation="deployment uses ACR connection"), self.assertRaises(PipelinePolicyError):
            validate_pipeline_policy(pipeline, build_template, swapped_deploy)


if __name__ == "__main__":
    unittest.main()
