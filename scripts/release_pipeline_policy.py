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
_ALLOWED_ROOT_STAGES = ("Validate",)
_AZURE_MUTATION_MARKERS = re.compile(
    r"(?im)^\s*- task:\s*(?:AzureCLI|AzurePowerShell|AzureResourceManagerTemplateDeployment|"
    r"AzureWebApp|AzureFunctionApp)@|"
    r"\b(?:az deployment (?:group|sub|tenant|mg)|az containerapp update|az webapp deploy|"
    r"az acr (?:login|import)|azd (?:provision|deploy)|terraform apply|bicep deploy|kubectl apply)\b"
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
    nested_stages = re.findall(r"(?m)^  - stage:\s*([A-Za-z0-9_]+)\s*$", conditional_block)
    if tuple(nested_stages) != _DELIVERY_STAGES:
        raise PipelinePolicyError("The guarded block may contain only the approved release stages")
    root_stages = re.findall(r"(?m)^- stage:\s*([A-Za-z0-9_]+)\s*$", pipeline)
    if tuple(root_stages) != _ALLOWED_ROOT_STAGES:
        raise PipelinePolicyError("All release stages must remain nested under the guarded opt-in block")
    validate_stage = _root_stage_block(pipeline, "Validate")
    if _AZURE_MUTATION_MARKERS.search(validate_stage):
        raise PipelinePolicyError("Azure mutations must remain inside the guarded release stage block")
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

    build_release = _stage_block(pipeline, "BuildRelease")
    deploy_dev = _stage_block(pipeline, "DeployDev")
    if (not _has_field(build_release, "acrPushServiceConnection", "${{ parameters.acrPushServiceConnection }}", 10) or
            not _has_field(deploy_dev, "devDeploymentServiceConnection", "${{ parameters.devDeploymentServiceConnection }}", 16) or
            not _has_task_input(build_template, "${{ parameters.acrPushServiceConnection }}") or
            not _has_task_input(deploy_template, "${{ parameters.devDeploymentServiceConnection }}")):
        raise PipelinePolicyError("Build/push and deployment must use their respective distinct connection inputs")
    if _has_task_input(build_template, "${{ parameters.devDeploymentServiceConnection }}"):
        raise PipelinePolicyError("The image build template must not use the DEV deployment connection")
    if _has_task_input(deploy_template, "${{ parameters.acrPushServiceConnection }}"):
        raise PipelinePolicyError("The DEV deployment template must not use the ACR push connection")


def _has_field(source: str, name: str, value: str, indentation: int) -> bool:
    return re.search(rf"(?m)^ {{{indentation}}}{re.escape(name)}:\s*{re.escape(value)}\s*$", source) is not None


def _has_task_input(template: str, value: str) -> bool:
    task_starts = list(re.finditer(r"(?m)^- task: AzureCLI@2\s*$", template))
    if len(task_starts) != 1:
        return False
    start = task_starts[0]
    following = re.search(r"(?m)^- ", template[start.end():])
    end = start.end() + following.start() if following else len(template)
    task = template[start.start():end]
    return _has_field(task, "azureSubscription", value, 4)


def _stage_block(pipeline: str, stage: str) -> str:
    match = re.search(rf"(?m)^  - stage: {re.escape(stage)}\s*$", pipeline)
    if match is None:
        raise PipelinePolicyError(f"Missing release stage {stage}")
    following = re.search(r"(?m)^  - stage: ", pipeline[match.end():])
    end = match.end() + following.start() if following else len(pipeline)
    return pipeline[match.start():end]


def _root_stage_block(pipeline: str, stage: str) -> str:
    match = re.search(rf"(?m)^- stage: {re.escape(stage)}\s*$", pipeline)
    if match is None:
        raise PipelinePolicyError(f"Missing root stage {stage}")
    following = re.search(r"(?m)^- (?:stage:|\$\{\{)", pipeline[match.end():])
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
