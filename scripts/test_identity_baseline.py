"""Static guards for the identity configuration currently present in this repo.

These tests validate source declarations only. They do not prove Azure RBAC,
federation, token acquisition, database grants, or denied operations in DEV.
"""
import json
import contextlib
import importlib.util
import io
from email.message import Message
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

from scripts.release_pipeline_policy import PipelinePolicyError, validate_pipeline_policy

ROOT = Path(__file__).resolve().parents[1]
_VALIDATION_TEMPLATE_PATHS = (
    "templates/validate-dotnet.yml",
    "templates/validate-frontend.yml",
    "templates/validate-infra.yml",
)


def _validate_pipeline_policy(
    pipeline,
    build_template,
    deploy_template,
    validation_template_overrides=None,
    release_helper_overrides=None,
):
    validation_templates = {
        name: (ROOT / "pipelines" / name).read_text()
        for name in _VALIDATION_TEMPLATE_PATHS
    }
    if validation_template_overrides:
        validation_templates.update(validation_template_overrides)
    release_helpers = {
        path: (ROOT / path).read_text()
        for path in (
            "scripts/validate_release_identity_inputs.py", "scripts/api-smoke.py",
            "scripts/private-dev-plan.sh", "scripts/private-dev-deploy.sh",
            "scripts/validate_private_dev_network.py", "scripts/validate_private_dev_whatif.py",
            "pipelines/templates/preview-private-dev.yml", "pipelines/templates/provision-private-dev.yml",
            "infra/private-dev-topology.bicep",
        )
    }
    if release_helper_overrides:
        release_helpers.update(release_helper_overrides)
    validate_pipeline_policy(pipeline, build_template, deploy_template, validation_templates, release_helpers)


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

    def test_azure_worker_role_is_verified_without_altering_managed_role(self):
        worker_role = (ROOT / "src/PitchMind.Infrastructure/Storage/AzureWorkerRoleProvisioner.cs").read_text()

        self.assertIn("pgaadauth_create_principal_with_oid(@role, @oid, 'service', false, false)", worker_role)
        self.assertNotIn("ALTER ROLE pitchmind_worker", worker_role)
        for privilege in ("rolsuper", "rolcreatedb", "rolcreaterole", "rolreplication", "rolbypassrls"):
            self.assertRegex(worker_role, rf"NOT {privilege}\b")
        self.assertIn("FROM pg_auth_members membership", worker_role)
        self.assertIn("has privileges beyond its restricted event-ingress grants", worker_role)

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
        _validate_pipeline_policy(pipeline, build_template, deploy_template)

    def test_policy_rejects_unreviewed_release_template_steps_and_scripts(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        mutations = {
            "extra build task": (
                build_template.replace(
                    "- publish: $(Build.ArtifactStagingDirectory)/release",
                    "- task: AzurePowerShell@5\n  inputs:\n    azureSubscription: malicious\n- publish: $(Build.ArtifactStagingDirectory)/release",
                    1,
                ), deploy_template,
            ),
            "build script mutation": (
                build_template.replace(
                    '      docker push "$loginServer/$repository:$tag"',
                    '      docker push "$loginServer/$repository:$tag"\n      az group delete --name unreviewed',
                    1,
                ), deploy_template,
            ),
            "deployment script mutation": (
                build_template,
                deploy_template.replace(
                    '      az containerapp update --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP"',
                    '      az group delete --name unreviewed\n      az containerapp update --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP"',
                    1,
                ),
            ),
            "extra build task input": (
                build_template.replace(
                    "    scriptLocation: inlineScript",
                    "    scriptLocation: inlineScript\n    workingDirectory: $(Pipeline.Workspace)",
                    1,
                ), deploy_template,
            ),
            "shell interpolation restored": (
                build_template.replace(
                    'registry="$PITCHMIND_REGISTRY_NAME"',
                    "registry='${{ parameters.registryName }}'",
                    1,
                ), deploy_template,
            ),
        }
        for mutation, (candidate_build, candidate_deploy) in mutations.items():
            with self.subTest(mutation=mutation), self.assertRaises(PipelinePolicyError):
                _validate_pipeline_policy(pipeline, candidate_build, candidate_deploy)

    def test_policy_requires_resource_names_to_reach_the_preflight(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        wrong_resource_mapping = pipeline.replace(
            "PITCHMIND_DEV_RESOURCE_GROUP: ${{ parameters.devResourceGroup }}",
            "PITCHMIND_DEV_RESOURCE_GROUP: ${{ parameters.devAppName }}",
            1,
        )
        with self.subTest(mutation="resource group is not validated"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(wrong_resource_mapping, build_template, deploy_template)

        unsafe_parameter_type = pipeline.replace(
            "- name: registryName\n  type: string",
            "- name: registryName\n  type: object",
            1,
        )
        with self.subTest(mutation="registry name is not a string parameter"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(unsafe_parameter_type, build_template, deploy_template)

    def test_policy_pins_release_helper_sources(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()
        helper_mutations = {
            "identity validator bypass": {
                "scripts/validate_release_identity_inputs.py": "def main():\n    return 0\n"
            },
            "smoke helper adds Azure mutation": {
                "scripts/api-smoke.py": "import subprocess\nsubprocess.run(['az', 'group', 'delete', '--name', 'target'])\n"
            },
        }
        for mutation, overrides in helper_mutations.items():
            with self.subTest(mutation=mutation), self.assertRaises(PipelinePolicyError):
                _validate_pipeline_policy(
                    pipeline, build_template, deploy_template, release_helper_overrides=overrides
                )

    def test_policy_mutations_reject_default_on_and_pull_request_delivery(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        invalid_delivery_type = pipeline.replace("- name: enableDevDelivery\n  type: boolean", "- name: enableDevDelivery\n  type: object", 1)
        with self.subTest(mutation="delivery type is not boolean"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(invalid_delivery_type, build_template, deploy_template)

        pr_eligible = pipeline.replace(
            "ne(variables['Build.Reason'], 'PullRequest')",
            "eq(variables['Build.Reason'], 'PullRequest')",
            1,
        )
        with self.subTest(mutation="owner approval stage PR condition"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pr_eligible, build_template, deploy_template)

        pr_eligible_build = pipeline.replace(
            "ne(variables['Build.Reason'], 'PullRequest')",
            "eq(variables['Build.Reason'], 'PullRequest')",
            2,
        )
        with self.subTest(mutation="build stage PR condition"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pr_eligible_build, build_template, deploy_template)

        unguarded_deploy = pipeline.replace(
            "  - stage: DeployDev\n    dependsOn: OwnerApprovalOfReleaseDigest",
            "  - stage: DeployDev\n    dependsOn: Validate",
            1,
        )
        with self.subTest(mutation="DEV deployment bypasses guarded release chain"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(unguarded_deploy, build_template, deploy_template)

        build_bypasses_approval = pipeline.replace(
            "  - stage: BuildRelease\n    dependsOn: OwnerApprovalBeforeAzureMutation",
            "  - stage: BuildRelease\n    dependsOn: Validate",
            1,
        )
        with self.subTest(mutation="image build bypasses pre-mutation owner approval"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(build_bypasses_approval, build_template, deploy_template)

        approval_bypasses_validation = pipeline.replace(
            "  - stage: OwnerApprovalBeforeAzureMutation\n    dependsOn: Validate",
            "  - stage: OwnerApprovalBeforeAzureMutation\n    dependsOn: BuildRelease",
            1,
        )
        with self.subTest(mutation="pre-mutation approval bypasses validation"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(approval_bypasses_validation, build_template, deploy_template)

    def test_candidate_feed_defaults_on_while_database_and_redis_provisioning_stay_off(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        for parameter, current, invalid in (
            ("enableCandidateFeed", "true", "false"),
            ("provisionPostgres", "false", "true"),
            ("provisionRedis", "false", "true"),
        ):
            candidate = pipeline.replace(
                f"- name: {parameter}\n  type: boolean\n  default: {current}",
                f"- name: {parameter}\n  type: boolean\n  default: {invalid}",
                1,
            )
            with self.subTest(parameter=parameter, default=invalid), self.assertRaises(PipelinePolicyError):
                _validate_pipeline_policy(candidate, build_template, deploy_template)

    def test_policy_requires_both_manual_approval_tasks(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        with_manual_pre_push = pipeline.replace(
            "          PITCHMIND_DEV_ESTIMATED_MONTHLY_COST: ${{ parameters.devEstimatedMonthlyCost }}",
            "          PITCHMIND_DEV_ESTIMATED_MONTHLY_COST: ${{ parameters.devEstimatedMonthlyCost }}\n"
            "    - job: AwaitExplicitOwnerApproval\n"
            "      dependsOn: ValidateReleaseIdentityInputs\n"
            "      displayName: Review the approved change set before ACR push or DEV mutation\n"
            "      pool: server\n"
            "      timeoutInMinutes: 1440\n"
            "      steps:\n"
            "      - task: ManualValidation@0\n"
            "        timeoutInMinutes: 1440\n"
            "        inputs:\n"
            "          notifyUsers: ''\n"
            "          instructions: |\n"
            "            STOP. Obtain and record explicit repository-owner approval for THIS story/change set.\n"
            "          onTimeout: reject",
            1,
        )
        task = "- task: ManualValidation@0"
        before_push_missing = with_manual_pre_push.replace(task, "- task: Bash@3", 1)
        with self.subTest(mutation="pre-push approval task corrupted"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(before_push_missing, build_template, deploy_template)

        with_both_manual = with_manual_pre_push.replace(
            "          estimatedMonthlyCost: ${{ parameters.devEstimatedMonthlyCost }}",
            "          estimatedMonthlyCost: ${{ parameters.devEstimatedMonthlyCost }}\n"
            "    - job: AwaitDigestConfirmation\n"
            "      dependsOn: PreviewPrivateDevTopology\n"
            "      displayName: Owner reconfirms the immutable image and DEV deployment\n"
            "      pool: server\n"
            "      timeoutInMinutes: 1440\n"
            "      steps:\n"
            "      - task: ManualValidation@0\n"
            "        timeoutInMinutes: 1440\n"
            "        inputs:\n"
            "          notifyUsers: ''\n"
            "          instructions: |\n"
            "            STOP. Inspect the release manifest image digest published by BuildRelease.\n"
            "            Reconfirm explicit owner approval.\n"
            "          onTimeout: reject",
            1,
        )
        digest_before, separator, digest_after = with_both_manual.rpartition(task)
        self.assertTrue(separator)
        digest_missing = digest_before + "- task: Bash@3" + digest_after
        with self.subTest(mutation="digest approval task corrupted"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(digest_missing, build_template, deploy_template)

    def test_policy_rejects_extra_mutation_jobs_or_steps_around_release_approvals(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        mutations = {
            "extra preflight-stage job": pipeline.replace(
                "    - job: ValidateReleaseIdentityInputs",
                "    - job: UnauthorizedMutation\n      steps:\n      - task: AzureCLI@2\n    - job: ValidateReleaseIdentityInputs",
                1,
            ),
            "mutation step in preflight job": pipeline.replace(
                "      - task: Bash@3\n        displayName: Require three separate release identities and reviewed network/cost inputs",
                "      - task: AzureCLI@2\n      - task: Bash@3\n        displayName: Require three separate release identities and reviewed network/cost inputs",
                1,
            ),
            "extra BuildRelease job": pipeline.replace(
                "    - job: Image\n      steps:",
                "    - job: UnauthorizedMutation\n      steps:\n      - task: AzureCLI@2\n    - job: Image\n      steps:",
                1,
            ),
            "mutation step in BuildRelease image job": pipeline.replace(
                "    - job: Image\n      steps:\n      - checkout: self",
                "    - job: Image\n      steps:\n      - task: AzureCLI@2\n      - checkout: self",
                1,
            ),
            "extra digest-approval job": pipeline.replace(
                "    - job: PreviewPrivateDevTopology",
                "    - job: UnauthorizedMutation\n      steps:\n      - task: AzureCLI@2\n    - job: PreviewPrivateDevTopology",
                1,
            ),
            "unreviewed DeployDev lifecycle hook": pipeline.replace(
                "        runOnce:\n          deploy:\n            steps:",
                "        runOnce:\n          preDeploy:\n            steps:\n            - task: AzureCLI@2\n          deploy:\n            steps:",
                1,
            ),
        }
        for mutation, candidate in mutations.items():
            with self.subTest(mutation=mutation), self.assertRaises(PipelinePolicyError):
                _validate_pipeline_policy(candidate, build_template, deploy_template)

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
            _validate_pipeline_policy(commented_opt_in, build_template, deploy_template)

        commented_conditions = pipeline.replace(
            "    condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'), ne(variables['Build.Reason'], 'PullRequest'))",
            "    # condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'), ne(variables['Build.Reason'], 'PullRequest'))",
            2,
        )
        with self.subTest(mutation="main and PR conditions exist only as comments"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(commented_conditions, build_template, deploy_template)

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
            _validate_pipeline_policy(pipeline, swapped_build, deploy_template)

        swapped_deploy = deploy_template.replace(
            "azureSubscription: ${{ parameters.devDeploymentServiceConnection }}",
            "azureSubscription: ${{ parameters.acrPushServiceConnection }}",
            1,
        )
        with self.subTest(mutation="deployment uses ACR connection"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pipeline, build_template, swapped_deploy)

        scalar_spoof = build_template.replace(
            "azureSubscription: ${{ parameters.acrPushServiceConnection }}",
            'azureSubscription: ${{ parameters.devDeploymentServiceConnection }}\n    displayName: "azureSubscription: ${{ parameters.acrPushServiceConnection }}"',
            1,
        )
        with self.subTest(mutation="connection text in displayName cannot spoof task input"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pipeline, scalar_spoof, deploy_template)

        pass_through_spoof = pipeline.replace(
            "          acrPushServiceConnection: ${{ parameters.acrPushServiceConnection }}",
            '          acrPushServiceConnection: ${{ parameters.devDeploymentServiceConnection }}\n          displayName: "acrPushServiceConnection: ${{ parameters.acrPushServiceConnection }}"',
            1,
        )
        with self.subTest(mutation="connection text in displayName cannot spoof template parameter"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pass_through_spoof, build_template, deploy_template)

        block_scalar_spoof = build_template.replace(
            "  displayName: Build immutable application image and release manifest",
            "  displayName: |\n    azureSubscription: ${{ parameters.acrPushServiceConnection }}",
            1,
        )
        with self.subTest(mutation="connection text in block scalar cannot spoof task input"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pipeline, block_scalar_spoof, deploy_template)

        wrong_task_parent = build_template.replace(
            "    azureSubscription: ${{ parameters.acrPushServiceConnection }}",
            "    azureSubscription: ${{ parameters.devDeploymentServiceConnection }}\n  env:\n    azureSubscription: ${{ parameters.acrPushServiceConnection }}",
            1,
        )
        with self.subTest(mutation="azureSubscription under env cannot spoof task inputs"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pipeline, wrong_task_parent, deploy_template)

        pass_through_block_scalar = pipeline.replace(
            "        parameters:\n          acrPushServiceConnection: ${{ parameters.acrPushServiceConnection }}",
            "        displayName: |\n          acrPushServiceConnection: ${{ parameters.acrPushServiceConnection }}\n        parameters:\n          acrPushServiceConnection: ${{ parameters.devDeploymentServiceConnection }}",
            1,
        )
        with self.subTest(mutation="connection text in block scalar cannot spoof template parameter"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pass_through_block_scalar, build_template, deploy_template)

        wrong_template_parent = pipeline.replace(
            "          acrPushServiceConnection: ${{ parameters.acrPushServiceConnection }}",
            "          acrPushServiceConnection: ${{ parameters.devDeploymentServiceConnection }}\n        env:\n          acrPushServiceConnection: ${{ parameters.acrPushServiceConnection }}",
            1,
        )
        with self.subTest(mutation="connection under template env cannot spoof parameters"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(wrong_template_parent, build_template, deploy_template)

    def test_policy_rejects_azure_mutation_hidden_in_unreviewed_bash_shortcut(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        injected_shortcut = (
            "    - bash: python3 -c \"import subprocess; "
            "subprocess.run(['az','group','delete','--name','unapproved'], check=True)\"\n"
            "      displayName: Unreviewed validation shortcut\n"
        )
        candidate = pipeline.replace(
            "    - checkout: self\n    - bash: |",
            "    - checkout: self\n" + injected_shortcut + "    - bash: |",
            1,
        )
        with self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(pipeline=candidate, build_template=build_template, deploy_template=deploy_template)

    def test_policy_rejects_unrecognized_unguarded_azure_mutation(self):
        pipeline = (ROOT / "pipelines/azure-pipelines.yml").read_text()
        build_template = (ROOT / "pipelines/templates/build-image.yml").read_text()
        deploy_template = (ROOT / "pipelines/templates/deploy-image.yml").read_text()

        direct_stage = pipeline.replace(
            "- ${{ if eq(parameters.enableDevDelivery, true) }}:",
            "- stage: DirectDeploy\n  jobs:\n  - job: Deploy\n    steps:\n    - task: AzureCLI@2\n- ${{ if eq(parameters.enableDevDelivery, true) }}:",
            1,
        )
        with self.subTest(mutation="new root delivery stage outside opt-in"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(direct_stage, build_template, deploy_template)

        extra_guarded_stage = pipeline.replace(
            "  - stage: OwnerApprovalBeforeAzureMutation",
            "  - stage: UnreviewedRelease\n    jobs:\n    - job: Deploy\n      steps:\n      - task: AzureCLI@2\n  - stage: OwnerApprovalBeforeAzureMutation",
            1,
        )
        with self.subTest(mutation="unrecognized release stage inside opt-in"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(extra_guarded_stage, build_template, deploy_template)

        mutation_in_validation = pipeline.replace(
            "- stage: Validate\n",
            "- stage: Validate\n  - task: AzureCLI@2\n",
            1,
        )
        with self.subTest(mutation="Azure CLI task outside the guarded release block"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(mutation_in_validation, build_template, deploy_template)

        mutating_validation_scripts = (
            "az rest --method PUT --url https://management.azure.com/example",
            "az group delete --name example",
            "New-AzResourceGroupDeployment -ResourceGroupName example",
            "terraform destroy -auto-approve",
        )
        for command in mutating_validation_scripts:
            with self.subTest(mutation=f"Azure-capable validation template: {command}"), self.assertRaises(PipelinePolicyError):
                _validate_pipeline_policy(
                    pipeline,
                    build_template,
                    deploy_template,
                    {"templates/validate-infra.yml": f"steps:\n- bash: |\n    {command}\n"},
                )

        with self.subTest(mutation="Azure task in Validate stage"), self.assertRaises(PipelinePolicyError):
            azure_task_in_validate = pipeline.replace(
                "    - bash: |",
                "    - task: AzureCLI@2\n      inputs:\n        azureSubscription: example\n    - bash: |",
                1,
            )
            _validate_pipeline_policy(azure_task_in_validate, build_template, deploy_template)

        new_validation_template = pipeline.replace(
            "- template: templates/validate-infra.yml",
            "- template: templates/unreviewed-validation.yml",
            1,
        )
        with self.subTest(mutation="unreviewed validation template indirection"), self.assertRaises(PipelinePolicyError):
            _validate_pipeline_policy(new_validation_template, build_template, deploy_template)


class PrivateDevWhatIfTests(unittest.TestCase):
    def run_validator(self, changes):
        with tempfile.TemporaryDirectory() as directory:
            plan_path = Path(directory) / "what-if.json"
            plan_path.write_text(json.dumps({"changes": changes}), encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(ROOT / "scripts/validate_private_dev_whatif.py"), str(plan_path)],
                capture_output=True,
                text=True,
                check=False,
            )

    def test_whatif_accepts_only_declared_candidate_event_hub_and_group(self):
        resource_group = "/subscriptions/sub/resourceGroups/rg-pitchmind-dev-001"
        changes = [
            {
                "changeType": "Create",
                "resourceType": "Microsoft.EventHub/namespaces/eventhubs",
                "resourceId": f"{resource_group}/providers/Microsoft.EventHub/namespaces/pm-dev-eh/eventhubs/candidate-match-events",
            },
            {
                "changeType": "Create",
                "resourceType": "Microsoft.EventHub/namespaces/eventhubs/consumergroups",
                "resourceId": f"{resource_group}/providers/Microsoft.EventHub/namespaces/pm-dev-eh/eventhubs/candidate-match-events/consumergroups/pitchmind-candidate-worker",
            },
        ]

        result = self.run_validator(changes)

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("no deletes/out-of-scope types", result.stdout)

    def test_whatif_accepts_candidate_feed_resources_only_at_their_declared_scopes(self):
        resource_group = "/subscriptions/sub/resourceGroups/rg-pitchmind-dev-001"
        changes = [
            {
                "changeType": "Create",
                "resourceType": "Microsoft.SignalRService/signalR",
                "resourceId": f"{resource_group}/providers/Microsoft.SignalRService/signalR/pm-dev-signalr",
            },
            {
                "changeType": "Create",
                "resourceType": "Microsoft.Storage/storageAccounts/blobServices/containers",
                "resourceId": f"{resource_group}/providers/Microsoft.Storage/storageAccounts/pmdev4iwntkatliy6sst/blobServices/default/containers/candidate-checkpoints",
            },
            {
                "changeType": "Create",
                "resourceType": "Microsoft.Authorization/roleAssignments",
                "resourceId": f"{resource_group}/providers/Microsoft.EventHub/namespaces/pm-dev-eh/eventhubs/candidate-match-events/providers/Microsoft.Authorization/roleAssignments/a1",
            },
            {
                "changeType": "Create",
                "resourceType": "Microsoft.Authorization/roleAssignments",
                "resourceId": f"{resource_group}/providers/Microsoft.Storage/storageAccounts/pmdev4iwntkatliy6sst/blobServices/default/containers/candidate-checkpoints/providers/Microsoft.Authorization/roleAssignments/a2",
            },
            {
                "changeType": "Create",
                "resourceType": "Microsoft.Authorization/roleAssignments",
                "resourceId": f"{resource_group}/providers/Microsoft.SignalRService/signalR/pm-dev-signalr/providers/Microsoft.Authorization/roleAssignments/a3",
            },
            {
                "changeType": "Create",
                "resourceType": "Microsoft.Cache/redisEnterprise/databases/accessPolicyAssignments",
                "resourceId": f"{resource_group}/providers/Microsoft.Cache/redisEnterprise/pm-dev-redis/databases/default/accessPolicyAssignments/pitchmind-api-cursor-cache",
            },
        ]

        result = self.run_validator(changes)

        self.assertEqual(result.returncode, 0, result.stderr)

    def test_whatif_rejects_candidate_role_assignment_outside_approved_resource_scopes(self):
        resource_group = "/subscriptions/sub/resourceGroups/rg-pitchmind-dev-001"
        changes = [{
            "changeType": "Create",
            "resourceType": "Microsoft.Authorization/roleAssignments",
            "resourceId": f"{resource_group}/providers/Microsoft.Storage/storageAccounts/unapproved/blobServices/default/containers/candidate-checkpoints/providers/Microsoft.Authorization/roleAssignments/a1",
        }]

        result = self.run_validator(changes)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Role assignment is outside", result.stderr)

    def test_whatif_accepts_only_the_declared_runtime_identity_and_database_job(self):
        resource_group = "/subscriptions/sub/resourceGroups/rg-pitchmind-dev-001"
        changes = [
            {
                "changeType": "Create",
                "resourceType": "Microsoft.ManagedIdentity/userAssignedIdentities",
                "resourceId": f"{resource_group}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/pm-dev-api-vnet-runtime",
            },
            {
                "changeType": "Create",
                "resourceType": "Microsoft.App/jobs",
                "resourceId": f"{resource_group}/providers/Microsoft.App/jobs/pm-dev-api-vnet-db-migrator",
            },
        ]

        result = self.run_validator(changes)

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("no deletes/out-of-scope types", result.stdout)

    def test_whatif_rejects_unapproved_database_job(self):
        resource_group = "/subscriptions/sub/resourceGroups/rg-pitchmind-dev-001"
        result = self.run_validator([{
            "changeType": "Create",
            "resourceType": "Microsoft.App/jobs",
            "resourceId": f"{resource_group}/providers/Microsoft.App/jobs/other-app-db-migrator",
        }])

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("approved private DEV resource name", result.stderr)

    def test_whatif_rejects_unapproved_event_hub_namespaces_hubs_and_groups(self):
        resource_group = "/subscriptions/sub/resourceGroups/rg-pitchmind-dev-001"
        mutations = [
            ("Microsoft.EventHub/namespaces/eventhubs", "other-namespace", "candidate-match-events"),
            ("Microsoft.EventHub/namespaces/eventhubs", "pm-dev-eh", "other-hub"),
            ("Microsoft.EventHub/namespaces/eventhubs/consumergroups", "pm-dev-eh", "candidate-match-events/consumergroups/other-group"),
        ]
        for resource_type, namespace, child_path in mutations:
            with self.subTest(resource_type=resource_type, namespace=namespace, child_path=child_path):
                changes = [{
                    "changeType": "Create",
                    "resourceType": resource_type,
                    "resourceId": f"{resource_group}/providers/Microsoft.EventHub/namespaces/{namespace}/eventhubs/{child_path}",
                }]
                result = self.run_validator(changes)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("outside the approved", result.stderr)


class ApiSmokeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location("pitchmind_api_smoke", ROOT / "scripts/api-smoke.py")
        cls.smoke = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.smoke)

    def run_smoke(self, response_code, azure_mode):
        received_headers = []

        class Response(io.BytesIO):
            def __init__(self, status, headers, body):
                super().__init__(body if isinstance(body, bytes) else json.dumps(body).encode())
                self.status = status
                self.headers = Message()
                for name, value in headers.items():
                    self.headers[name] = value

        def open_url(request, timeout):
            self.assertEqual(timeout, 10)
            headers = {name.casefold(): value for name, value in request.header_items()}
            path = request.full_url.removeprefix("http://smoke")
            received_headers.append((path, headers))
            correlation = "smoke-correlation"
            response_headers = {"X-Correlation-ID": correlation}
            if path.startswith("/api/v1/"):
                response_headers["X-Contract-Version"] = "1"
                expected_status = 401 if azure_mode else 503
                payload = {
                    "type": "about:blank",
                    "title": "Authentication required" if azure_mode else "API unavailable",
                    "status": expected_status,
                    "code": response_code,
                    "correlationId": correlation,
                    "instance": path,
                }
                response_headers["Content-Type"] = "application/problem+json"
                return Response(expected_status, response_headers, payload)
            response_headers["Content-Type"] = "text/html; charset=utf-8" if path == "/" else "application/json"
            if path == "/":
                return Response(200, response_headers, b"<!doctype html><html></html>")
            return Response(200, response_headers, {"status": "ok"})

        argv = ["http://smoke"]
        if azure_mode:
            argv.insert(0, "--expect-auth-required")
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            exit_code = self.smoke.main(argv, open_url=open_url)
        return exit_code, output.getvalue(), received_headers

    def test_local_smoke_uses_development_identity_and_match_unavailable_contract(self):
        exit_code, _output, headers = self.run_smoke("MATCH_DATA_UNAVAILABLE", azure_mode=False)

        self.assertEqual(exit_code, 0)
        api_requests = [request for path, request in headers if path.startswith("/api/v1/")]
        self.assertTrue(all("x-dev-subject" in request for request in api_requests))
        self.assertTrue(all("x-dev-roles" in request for request in api_requests))

    def test_azure_smoke_expects_fail_closed_identity_without_development_headers(self):
        exit_code, output, headers = self.run_smoke("UNAUTHORIZED", azure_mode=True)

        self.assertEqual(exit_code, 0)
        api_requests = [request for path, request in headers if path.startswith("/api/v1/")]
        self.assertTrue(all("x-dev-subject" not in request for request in api_requests))
        self.assertTrue(all("x-dev-roles" not in request for request in api_requests))
        self.assertIn("anonymous requests", output)

    def test_azure_smoke_does_not_mask_wrong_fail_closed_response(self):
        with self.assertRaisesRegex(AssertionError, "MATCH_DATA_UNAVAILABLE"):
            self.run_smoke("MATCH_DATA_UNAVAILABLE", azure_mode=True)



if __name__ == "__main__":
    unittest.main()
