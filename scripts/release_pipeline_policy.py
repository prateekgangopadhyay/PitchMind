"""Structural checks for the opt-in Azure release path.

These checks validate parsed repository YAML only. They do not prove Azure
DevOps federation, RBAC, protected-environment configuration, or owner approval.
"""
from __future__ import annotations

import re
from typing import Any

import yaml


class PipelinePolicyError(ValueError):
    pass


_OPT_IN = "${{ if eq(parameters.enableDevDelivery, true) }}"
_DELIVERY_STAGES = (
    "OwnerApprovalBeforeAzureMutation",
    "BuildRelease",
    "OwnerApprovalOfReleaseDigest",
    "DeployDev",
)
_VALIDATION_TEMPLATES = {
    "templates/validate-dotnet.yml",
    "templates/validate-frontend.yml",
    "templates/validate-infra.yml",
}
_ALLOWED_VALIDATION_TASKS = {
    "UseDotNet@2",
    "NodeTool@0",
    "PublishTestResults@2",
}
_AZ_COMMAND = re.compile(r"\baz\s+([a-z][a-z0-9-]*)\s*([a-z][a-z0-9-]*)?", re.IGNORECASE)
_AZ_MUTATING_CMDLETS = re.compile(r"\b[A-Za-z][A-Za-z0-9]*-Az[A-Za-z0-9]+\b")
_OTHER_MUTATORS = re.compile(
    r"\b(?:terraform\s+(?:apply|destroy)|bicep\s+deploy|kubectl\s+apply|azd\s+(?:provision|deploy))\b",
    re.IGNORECASE,
)
_ALLOWED_AZ_BICEP = {"version", "build", "build-params"}


def validate_pipeline_policy(
    pipeline: str,
    build_template: str,
    deploy_template: str,
    validation_templates: dict[str, str],
) -> None:
    """Reject policy weakening by inspecting active YAML mappings and steps."""
    pipeline_doc = _load_yaml(pipeline, "pipeline")
    build_doc = _load_yaml(build_template, "image build template")
    deploy_doc = _load_yaml(deploy_template, "deployment template")

    parameters = _sequence(pipeline_doc.get("parameters"), "pipeline parameters")
    delivery_parameter = next(
        (item for item in parameters if isinstance(item, dict) and item.get("name") == "enableDevDelivery"),
        None,
    )
    if delivery_parameter is None or delivery_parameter.get("type") != "boolean" or delivery_parameter.get("default") is not False:
        raise PipelinePolicyError("DEV delivery must remain a boolean default-off parameter")

    stage_items = _sequence(pipeline_doc.get("stages"), "pipeline stages")
    validate_stages = [item for item in stage_items if isinstance(item, dict) and item.get("stage") == "Validate"]
    conditional_items = [item for item in stage_items if isinstance(item, dict) and _OPT_IN in item]
    if len(validate_stages) != 1 or len(conditional_items) != 1 or len(stage_items) != 2:
        raise PipelinePolicyError("The pipeline may contain only Validate and one guarded release block")

    guarded_stages = _sequence(conditional_items[0][_OPT_IN], "guarded release stages")
    stage_names = [item.get("stage") for item in guarded_stages if isinstance(item, dict)]
    if tuple(stage_names) != _DELIVERY_STAGES or len(stage_names) != len(guarded_stages):
        raise PipelinePolicyError("The guarded block may contain only the approved release stages in order")
    stages_by_name = {item["stage"]: item for item in guarded_stages}

    main_only_condition = (
        "and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'), "
        "ne(variables['Build.Reason'], 'PullRequest'))"
    )
    for stage_name in ("OwnerApprovalBeforeAzureMutation", "BuildRelease"):
        if stages_by_name[stage_name].get("condition") != main_only_condition:
            raise PipelinePolicyError(f"{stage_name} must be restricted to main and exclude pull-request runs")

    digest_approval = stages_by_name["OwnerApprovalOfReleaseDigest"]
    if digest_approval.get("dependsOn") != "BuildRelease" or digest_approval.get("condition") != "succeeded()":
        raise PipelinePolicyError("Digest approval must run only after a successful guarded image build")
    dev_deployment = stages_by_name["DeployDev"]
    if dev_deployment.get("dependsOn") != "OwnerApprovalOfReleaseDigest" or dev_deployment.get("condition") != "succeeded()":
        raise PipelinePolicyError("DEV deployment must depend on successful digest approval")

    validate_stage = validate_stages[0]
    referenced_templates = _find_values(validate_stage, "template")
    if set(referenced_templates) != _VALIDATION_TEMPLATES or len(referenced_templates) != len(_VALIDATION_TEMPLATES):
        raise PipelinePolicyError("Validation stage template references must remain on the reviewed allowlist")
    for template_name in referenced_templates:
        template_source = validation_templates.get(template_name)
        if template_source is None:
            raise PipelinePolicyError(f"Missing source for validation template {template_name}")
        template_doc = _load_yaml(template_source, template_name)
        _assert_validation_only(validate_stage, "Validate stage")
        _assert_validation_only(template_doc, template_name)
        if _find_values(template_doc, "template"):
            raise PipelinePolicyError("Nested validation templates require an explicit policy review")

    build_release = stages_by_name["BuildRelease"]
    deploy_stage = stages_by_name["DeployDev"]
    build_call = _find_one_template_call(build_release, "templates/build-image.yml")
    deploy_call = _find_one_template_call(deploy_stage, "templates/deploy-image.yml")
    if build_call.get("parameters", {}).get("acrPushServiceConnection") != "${{ parameters.acrPushServiceConnection }}":
        raise PipelinePolicyError("BuildRelease must pass the dedicated ACR-push service connection")
    if deploy_call.get("parameters", {}).get("devDeploymentServiceConnection") != "${{ parameters.devDeploymentServiceConnection }}":
        raise PipelinePolicyError("DeployDev must pass the dedicated DEV deployment service connection")

    if _azure_cli_task_connection(build_doc) != "${{ parameters.acrPushServiceConnection }}":
        raise PipelinePolicyError("The image build task must use the ACR-push service connection")
    if _azure_cli_task_connection(deploy_doc) != "${{ parameters.devDeploymentServiceConnection }}":
        raise PipelinePolicyError("The deployment task must use the DEV deployment service connection")


def _load_yaml(source: str, label: str) -> dict[str, Any]:
    try:
        document = yaml.safe_load(source)
    except yaml.YAMLError as error:
        raise PipelinePolicyError(f"Could not parse {label} YAML") from error
    if not isinstance(document, dict):
        raise PipelinePolicyError(f"{label} YAML must have a mapping root")
    return document


def _sequence(value: Any, label: str) -> list[Any]:
    if not isinstance(value, list):
        raise PipelinePolicyError(f"{label} must be a sequence")
    return value


def _find_values(node: Any, key: str) -> list[Any]:
    found: list[Any] = []
    if isinstance(node, dict):
        for current_key, value in node.items():
            if current_key == key:
                found.append(value)
            found.extend(_find_values(value, key))
    elif isinstance(node, list):
        for value in node:
            found.extend(_find_values(value, key))
    return found


def _find_one_template_call(stage: dict[str, Any], template_name: str) -> dict[str, Any]:
    calls = [value for value in _find_values(stage, "template") if value == template_name]
    if len(calls) != 1:
        raise PipelinePolicyError(f"Stage must have exactly one call to {template_name}")
    return _find_template_call_mapping(stage, template_name)


def _find_template_call_mapping(node: Any, template_name: str) -> dict[str, Any]:
    matches: list[dict[str, Any]] = []
    if isinstance(node, dict):
        if node.get("template") == template_name:
            matches.append(node)
        for value in node.values():
            matches.extend(_find_template_call_mappings(value, template_name))
    elif isinstance(node, list):
        for value in node:
            matches.extend(_find_template_call_mappings(value, template_name))
    if len(matches) != 1:
        raise PipelinePolicyError(f"Stage must have exactly one mapping call to {template_name}")
    return matches[0]


def _find_template_call_mappings(node: Any, template_name: str) -> list[dict[str, Any]]:
    if isinstance(node, dict):
        results = [node] if node.get("template") == template_name else []
        for value in node.values():
            results.extend(_find_template_call_mappings(value, template_name))
        return results
    if isinstance(node, list):
        return [match for value in node for match in _find_template_call_mappings(value, template_name)]
    return []


def _azure_cli_task_connection(template_doc: dict[str, Any]) -> Any:
    matches: list[Any] = []
    for task in _find_values(template_doc, "task"):
        if task == "AzureCLI@2":
            # Inspect the task's active inputs mapping, not other YAML values.
            for node in _find_mappings(template_doc):
                if node.get("task") == task:
                    inputs = node.get("inputs")
                    if isinstance(inputs, dict):
                        matches.append(inputs.get("azureSubscription"))
    if len(matches) != 1:
        raise PipelinePolicyError("Each release template must contain exactly one AzureCLI@2 task")
    return matches[0]


def _find_mappings(node: Any) -> list[dict[str, Any]]:
    if isinstance(node, dict):
        return [node, *(mapping for value in node.values() for mapping in _find_mappings(value))]
    if isinstance(node, list):
        return [mapping for value in node for mapping in _find_mappings(value)]
    return []


def _assert_validation_only(node: Any, label: str) -> None:
    for task in _find_values(node, "task"):
        if isinstance(task, str) and task.startswith("Azure"):
            raise PipelinePolicyError(f"Azure task {task} is not allowed in {label}")
        if isinstance(task, str) and task not in _ALLOWED_VALIDATION_TASKS:
            raise PipelinePolicyError(f"Unreviewed task {task} is not allowed in {label}")
    for command in _script_values(node):
        if _OTHER_MUTATORS.search(command) or _AZ_MUTATING_CMDLETS.search(command):
            raise PipelinePolicyError(f"Potential infrastructure mutation is not allowed in {label}")
        for match in _AZ_COMMAND.finditer(command):
            if match.group(1).casefold() != "bicep" or (match.group(2) or "").casefold() not in _ALLOWED_AZ_BICEP:
                raise PipelinePolicyError(f"Only local read-only az bicep validation is allowed in {label}")


def _script_values(node: Any) -> list[str]:
    script_keys = {"bash", "script", "inlineScript", "powershell", "pwsh"}
    if isinstance(node, dict):
        return [
            value
            for key, child in node.items()
            for value in (([child] if key in script_keys and isinstance(child, str) else []) + _script_values(child))
        ]
    if isinstance(node, list):
        return [value for child in node for value in _script_values(child)]
    return []
