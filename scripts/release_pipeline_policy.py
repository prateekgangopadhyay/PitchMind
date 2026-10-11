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
    pipeline = _without_yaml_comments(pipeline)
    build_template = _without_yaml_comments(build_template)
    deploy_template = _without_yaml_comments(deploy_template)

    default_off = re.search(
        r"(?ms)^- name: enableDevDelivery\s+type: boolean\s+default: false\s*$",
        pipeline,
    )
    if default_off is None:
        raise PipelinePolicyError("DEV delivery must remain a boolean default-off parameter")

    opt_in = "- ${{ if eq(parameters.enableDevDelivery, true) }}:"
    if pipeline.count(opt_in) != 1:
        raise PipelinePolicyError("DEV release stages must have one explicit opt-in condition")
    conditional_block = _conditional_block(pipeline, opt_in)
    for stage in _DELIVERY_STAGES:
        if len(re.findall(rf"(?m)^  - stage: {re.escape(stage)}\s*$", conditional_block)) != 1:
            raise PipelinePolicyError(f"{stage} must be nested under the opt-in release condition")

    for stage in ("OwnerApprovalBeforeAzureMutation", "BuildRelease"):
        stage_text = _stage_block(pipeline, stage)
        main_only = "and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'), ne(variables['Build.Reason'], 'PullRequest'))"
        if not re.search(rf"(?m)^    condition:\s*{re.escape(main_only)}\s*$", stage_text):
            raise PipelinePolicyError(f"{stage} must be restricted to main and exclude pull-request runs")

    digest_approval = _stage_block(pipeline, "OwnerApprovalOfReleaseDigest")
    if (not re.search(r"(?m)^    dependsOn: BuildRelease\s*$", digest_approval) or
            not re.search(r"(?m)^    condition: succeeded\(\)\s*$", digest_approval)):
        raise PipelinePolicyError("Digest approval must run only after a successful guarded image build")
    dev_deployment = _stage_block(pipeline, "DeployDev")
    if (not re.search(r"(?m)^    dependsOn: OwnerApprovalOfReleaseDigest\s*$", dev_deployment) or
            not re.search(r"(?m)^    condition: succeeded\(\)\s*$", dev_deployment)):
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


def _conditional_block(pipeline: str, condition: str) -> str:
    """Return only active, indented stages beneath the root opt-in list item."""
    match = re.search(rf"(?m)^- {re.escape(condition[2:])}\s*$", pipeline)
    if match is None:
        raise PipelinePolicyError("DEV release stages must use an active root-level opt-in condition")
    following_root_item = re.search(r"(?m)^- (?:stage:|\$\{\{)", pipeline[match.end():])
    end = match.end() + following_root_item.start() if following_root_item else len(pipeline)
    return pipeline[match.end():end]


def _without_yaml_comments(source: str) -> str:
    """Strip YAML comments while preserving hashes inside quoted scalars."""
    cleaned_lines: list[str] = []
    for line in source.splitlines():
        quote: str | None = None
        escaped = False
        index = 0
        while index < len(line):
            character = line[index]
            if quote == '"':
                if escaped:
                    escaped = False
                elif character == "\\":
                    escaped = True
                elif character == '"':
                    quote = None
            elif quote == "'":
                if character == "'":
                    if index + 1 < len(line) and line[index + 1] == "'":
                        index += 2
                        continue
                    quote = None
            elif character in ('"', "'"):
                quote = character
            elif character == "#" and (index == 0 or line[index - 1].isspace()):
                line = line[:index]
                break
            index += 1
        cleaned_lines.append(line.rstrip())
    return "\n".join(cleaned_lines)
