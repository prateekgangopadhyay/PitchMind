"""Static policy checks for the opt-in Azure release path.

These checks validate repository text only. They do not prove Azure DevOps
service-connection federation, RBAC, protected-environment configuration, or
owner approval outside this repository.
"""
from __future__ import annotations

import re


class PipelinePolicyError(ValueError):
    pass


_DELIVERY_STAGES = (
    "OwnerApprovalBeforeAzureMutation",
    "BuildRelease",
    "OwnerApprovalOfReleaseDigest",
    "DeployDev",
)


def validate_pipeline_policy(pipeline: str, build_template: str, deploy_template: str) -> None:
    """Reject release-pipeline source edits that weaken identity and PR gates."""
    default_off = re.search(
        r"(?ms)^- name: enableDevDelivery\s+type: boolean\s+default: false\s*$",
        pipeline,
    )
    if default_off is None:
        raise PipelinePolicyError("DEV delivery must remain a boolean default-off parameter")

    opt_in = "- ${{ if eq(parameters.enableDevDelivery, true) }}:"
    if pipeline.count(opt_in) != 1:
        raise PipelinePolicyError("DEV release stages must have one explicit opt-in condition")
    for stage in _DELIVERY_STAGES:
        if len(re.findall(rf"(?m)^  - stage: {re.escape(stage)}\s*$", pipeline)) != 1:
            raise PipelinePolicyError(f"{stage} must be nested under the opt-in release condition")

    for stage in ("OwnerApprovalBeforeAzureMutation", "BuildRelease"):
        stage_text = _stage_block(pipeline, stage)
        if "eq(variables['Build.SourceBranch'], 'refs/heads/main')" not in stage_text:
            raise PipelinePolicyError(f"{stage} must be restricted to main")
        if "ne(variables['Build.Reason'], 'PullRequest')" not in stage_text:
            raise PipelinePolicyError(f"{stage} must explicitly exclude pull-request runs")

    digest_approval = _stage_block(pipeline, "OwnerApprovalOfReleaseDigest")
    if "dependsOn: BuildRelease" not in digest_approval or "condition: succeeded()" not in digest_approval:
        raise PipelinePolicyError("Digest approval must run only after a successful guarded image build")
    dev_deployment = _stage_block(pipeline, "DeployDev")
    if "dependsOn: OwnerApprovalOfReleaseDigest" not in dev_deployment or "condition: succeeded()" not in dev_deployment:
        raise PipelinePolicyError("DEV deployment must depend on successful digest approval")

    expected = (
        ("acrPushServiceConnection: ${{ parameters.acrPushServiceConnection }}", pipeline),
        ("devDeploymentServiceConnection: ${{ parameters.devDeploymentServiceConnection }}", pipeline),
        ("azureSubscription: ${{ parameters.acrPushServiceConnection }}", build_template),
        ("azureSubscription: ${{ parameters.devDeploymentServiceConnection }}", deploy_template),
    )
    if any(fragment not in source for fragment, source in expected):
        raise PipelinePolicyError("Build/push and deployment must use their respective distinct connection inputs")
    if "azureSubscription: ${{ parameters.devDeploymentServiceConnection }}" in build_template:
        raise PipelinePolicyError("The image build template must not use the DEV deployment connection")
    if "azureSubscription: ${{ parameters.acrPushServiceConnection }}" in deploy_template:
        raise PipelinePolicyError("The DEV deployment template must not use the ACR push connection")


def _stage_block(pipeline: str, stage: str) -> str:
    match = re.search(rf"(?m)^  - stage: {re.escape(stage)}\s*$", pipeline)
    if match is None:
        raise PipelinePolicyError(f"Missing release stage {stage}")
    following = re.search(r"(?m)^  - stage: ", pipeline[match.end():])
    end = match.end() + following.start() if following else len(pipeline)
    return pipeline[match.start():end]
