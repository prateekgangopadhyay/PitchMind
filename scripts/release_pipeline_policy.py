"""Structural checks for the opt-in Azure release path.

These checks validate parsed repository YAML only. They do not prove Azure
DevOps federation, RBAC, protected-environment configuration, or owner approval.
"""
from __future__ import annotations

import re
import hashlib
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
# Script changes in AzureCLI release templates require this policy pin to be
# reviewed alongside a command-level diff and the mutation tests.
_BUILD_SCRIPT_SHA256 = "04319433ce7b3517a9e8fcbd660b696fdc8dfac2e3c5575ec004de0379e55a8a"
_DEPLOY_SCRIPT_SHA256 = "28577f73b5910af3c6e078bfd252d601f1bbd194ac105920389a394ec2a6693c"


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
    for parameter_name in ("registryName", "devResourceGroup", "devAppName"):
        configured = [
            item for item in parameters
            if isinstance(item, dict) and item.get("name") == parameter_name
        ]
        if len(configured) != 1 or configured[0].get("type") != "string":
            raise PipelinePolicyError(f"{parameter_name} must remain a single queue-time string parameter")

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
    if stages_by_name["OwnerApprovalBeforeAzureMutation"].get("dependsOn") != "Validate":
        raise PipelinePolicyError("Pre-mutation owner approval must depend on successful validation")
    if stages_by_name["BuildRelease"].get("dependsOn") != "OwnerApprovalBeforeAzureMutation":
        raise PipelinePolicyError("Image build/push must wait for pre-mutation owner approval")

    digest_approval = stages_by_name["OwnerApprovalOfReleaseDigest"]
    if digest_approval.get("dependsOn") != "BuildRelease" or digest_approval.get("condition") != "succeeded()":
        raise PipelinePolicyError("Digest approval must run only after a successful guarded image build")
    dev_deployment = stages_by_name["DeployDev"]
    if dev_deployment.get("dependsOn") != "OwnerApprovalOfReleaseDigest" or dev_deployment.get("condition") != "succeeded()":
        raise PipelinePolicyError("DEV deployment must depend on successful digest approval")

    _require_manual_approval(
        stages_by_name["OwnerApprovalBeforeAzureMutation"],
        job_name="AwaitExplicitOwnerApproval",
        depends_on="ValidateReleaseIdentityInputs",
        instruction_tokens=("explicit repository-owner approval", "this story/change set"),
    )
    _require_identity_preflight(stages_by_name["OwnerApprovalBeforeAzureMutation"])
    _require_release_stage_shape(
        stages_by_name["OwnerApprovalBeforeAzureMutation"],
        expected_jobs=("ValidateReleaseIdentityInputs", "AwaitExplicitOwnerApproval"),
    )
    _require_manual_approval(
        digest_approval,
        job_name="AwaitDigestConfirmation",
        depends_on=None,
        instruction_tokens=("release manifest image digest", "explicit owner approval"),
    )
    _require_release_stage_shape(digest_approval, expected_jobs=("AwaitDigestConfirmation",))

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
    _require_release_stage_shape(build_release, expected_jobs=("Image",))
    _require_release_stage_shape(deploy_stage, expected_jobs=("Dev",))
    build_call = _find_one_template_call(build_release, "templates/build-image.yml")
    deploy_call = _find_one_template_call(deploy_stage, "templates/deploy-image.yml")
    if build_call.get("parameters", {}).get("acrPushServiceConnection") != "${{ parameters.acrPushServiceConnection }}":
        raise PipelinePolicyError("BuildRelease must pass the dedicated ACR-push service connection")
    if deploy_call.get("parameters", {}).get("devDeploymentServiceConnection") != "${{ parameters.devDeploymentServiceConnection }}":
        raise PipelinePolicyError("DeployDev must pass the dedicated DEV deployment service connection")

    _assert_release_template(
        build_doc,
        kind="build",
        expected_connection="${{ parameters.acrPushServiceConnection }}",
    )
    _assert_release_template(
        deploy_doc,
        kind="deploy",
        expected_connection="${{ parameters.devDeploymentServiceConnection }}",
    )
    if build_call["parameters"].get("registryName") != "${{ parameters.registryName }}" or \
            build_call["parameters"].get("imageRepository") != "pitchmind":
        raise PipelinePolicyError("BuildRelease must pass only the reviewed registry and image repository")
    if deploy_call["parameters"].get("resourceGroup") != "${{ parameters.devResourceGroup }}" or \
            deploy_call["parameters"].get("appName") != "${{ parameters.devAppName }}":
        raise PipelinePolicyError("DeployDev must pass only the reviewed resource group and app name")


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


def _assert_release_template(template_doc: dict[str, Any], *, kind: str, expected_connection: str) -> None:
    parameters = template_doc.get("parameters")
    steps = template_doc.get("steps")
    expected_parameter_names = (
        ("acrPushServiceConnection", "registryName", "imageRepository")
        if kind == "build" else
        ("devDeploymentServiceConnection", "resourceGroup", "appName")
    )
    expected_parameters = (
        [
            {"name": "acrPushServiceConnection", "type": "string"},
            {"name": "registryName", "type": "string"},
            {"name": "imageRepository", "type": "string", "default": "pitchmind"},
        ]
        if kind == "build" else
        [
            {"name": "devDeploymentServiceConnection", "type": "string"},
            {"name": "resourceGroup", "type": "string"},
            {"name": "appName", "type": "string"},
        ]
    )
    if set(template_doc) != {"parameters", "steps"} or not isinstance(parameters, list) or \
            tuple(item.get("name") for item in parameters if isinstance(item, dict)) != expected_parameter_names or \
            parameters != expected_parameters:
        raise PipelinePolicyError(f"The {kind} release template parameters must remain on the reviewed allowlist")

    if not isinstance(steps, list) or len(steps) != 2:
        raise PipelinePolicyError(f"The {kind} release template may contain only its two reviewed steps")
    if kind == "build":
        task = steps[0]
        expected_env = {
            "PITCHMIND_REGISTRY_NAME": "${{ parameters.registryName }}",
            "PITCHMIND_IMAGE_REPOSITORY": "${{ parameters.imageRepository }}",
        }
        if not isinstance(steps[1], dict) or steps[1] != {
            "publish": "$(Build.ArtifactStagingDirectory)/release",
            "artifact": "release",
            "displayName": "Publish immutable release manifest",
        }:
            raise PipelinePolicyError("The image build template may contain only the reviewed manifest publication")
        expected_script_hash = _BUILD_SCRIPT_SHA256
        expected_display = "Build immutable application image and release manifest"
    elif kind == "deploy":
        if steps[0] != {"download": "current", "artifact": "release"}:
            raise PipelinePolicyError("The deployment template may only download the reviewed release artifact")
        task = steps[1]
        expected_env = {
            "PITCHMIND_DEV_RESOURCE_GROUP": "${{ parameters.resourceGroup }}",
            "PITCHMIND_DEV_APP_NAME": "${{ parameters.appName }}",
        }
        expected_script_hash = _DEPLOY_SCRIPT_SHA256
        expected_display = "Deploy the already built digest and prove HTTP smoke"
    else:
        raise PipelinePolicyError("Unknown release template kind")

    if not isinstance(task, dict) or set(task) != {"task", "displayName", "env", "inputs"} or \
            task.get("task") != "AzureCLI@2" or task.get("displayName") != expected_display:
        raise PipelinePolicyError(f"The {kind} release template may contain only its reviewed AzureCLI task")
    if task.get("env") != expected_env:
        raise PipelinePolicyError(f"The {kind} release template must pass resource names through reviewed environment variables")
    inputs = task.get("inputs")
    if not isinstance(inputs, dict) or set(inputs) != {"azureSubscription", "scriptType", "scriptLocation", "inlineScript"} or \
            inputs.get("azureSubscription") != expected_connection or inputs.get("scriptType") != "bash" or \
            inputs.get("scriptLocation") != "inlineScript" or not isinstance(inputs.get("inlineScript"), str):
        raise PipelinePolicyError(f"The {kind} AzureCLI inputs must remain on the reviewed allowlist")
    actual_script_hash = hashlib.sha256(inputs["inlineScript"].encode("utf-8")).hexdigest()
    if actual_script_hash != expected_script_hash:
        raise PipelinePolicyError(f"The {kind} inline AzureCLI script changed without a reviewed policy pin")


def _require_manual_approval(
    stage: dict[str, Any],
    *,
    job_name: str,
    depends_on: str | None,
    instruction_tokens: tuple[str, ...],
) -> None:
    jobs = _sequence(stage.get("jobs"), f"{stage.get('stage')} jobs")
    approvals = [job for job in jobs if isinstance(job, dict) and job.get("job") == job_name]
    if len(approvals) != 1:
        raise PipelinePolicyError(f"{stage.get('stage')} must contain exactly one {job_name} approval job")
    approval = approvals[0]
    approval_fields = {"job", "displayName", "pool", "timeoutInMinutes", "steps"}
    if depends_on is not None:
        approval_fields.add("dependsOn")
    if set(approval) != approval_fields:
        raise PipelinePolicyError(f"{job_name} may contain only its reviewed approval fields")
    if approval.get("pool") != "server":
        raise PipelinePolicyError(f"{job_name} must be an agentless server-side approval")
    if depends_on is not None and approval.get("dependsOn") != depends_on:
        raise PipelinePolicyError(f"{job_name} must follow {depends_on}")
    steps = _sequence(approval.get("steps"), f"{job_name} steps")
    if len(steps) != 1:
        raise PipelinePolicyError(f"{job_name} may contain only its manual approval task")
    tasks = [step for step in steps if isinstance(step, dict) and step.get("task") == "ManualValidation@0"]
    if len(tasks) != 1:
        raise PipelinePolicyError(f"{job_name} must contain exactly one ManualValidation@0 task")
    task = tasks[0]
    if set(task) != {"task", "timeoutInMinutes", "inputs"}:
        raise PipelinePolicyError(f"{job_name} manual approval task may contain only reviewed fields")
    inputs = task.get("inputs")
    instructions = inputs.get("instructions") if isinstance(inputs, dict) else None
    if (task.get("timeoutInMinutes") != 1440 or not isinstance(inputs, dict) or
            inputs.get("onTimeout") != "reject" or not isinstance(instructions, str) or
            set(inputs) != {"notifyUsers", "instructions", "onTimeout"} or
            any(token.casefold() not in instructions.casefold() for token in instruction_tokens)):
        raise PipelinePolicyError(f"{job_name} must have an explicit, rejecting owner approval instruction")


def _require_identity_preflight(stage: dict[str, Any]) -> None:
    jobs = _sequence(stage.get("jobs"), "pre-mutation approval jobs")
    matching_jobs = [job for job in jobs if isinstance(job, dict) and job.get("job") == "ValidateReleaseIdentityInputs"]
    if len(matching_jobs) != 1:
        raise PipelinePolicyError("Pre-mutation approval must include the Ubuntu identity-input preflight")
    if set(matching_jobs[0]) != {"job", "pool", "steps"}:
        raise PipelinePolicyError("Identity preflight job may contain only its reviewed fields")
    pool = matching_jobs[0].get("pool")
    if not isinstance(pool, dict) or pool.get("vmImage") != "ubuntu-latest":
        raise PipelinePolicyError("Pre-mutation approval must include the Ubuntu identity-input preflight")
    steps = _sequence(matching_jobs[0].get("steps"), "identity preflight steps")
    if len(steps) != 2 or steps[0] != {"checkout": "self"}:
        raise PipelinePolicyError("Identity preflight may contain only checkout and its identity validation task")
    tasks = [
        step for step in steps
        if isinstance(step, dict) and step.get("task") == "Bash@3" and
        isinstance(step.get("inputs"), dict) and
        step["inputs"].get("script") == "python3 scripts/validate_release_identity_inputs.py"
    ]
    if len(tasks) != 1:
        raise PipelinePolicyError("Pre-mutation approval must validate the release identity inputs")
    if set(tasks[0]) != {"task", "displayName", "inputs", "env"}:
        raise PipelinePolicyError("Identity preflight task may contain only its reviewed fields")
    if set(tasks[0]["inputs"]) != {"targetType", "script"} or tasks[0]["inputs"].get("targetType") != "inline":
        raise PipelinePolicyError("Identity preflight inputs may contain only the reviewed script")
    environment = tasks[0].get("env")
    expected = {
        "PITCHMIND_ACR_PUSH_SERVICE_CONNECTION": "${{ parameters.acrPushServiceConnection }}",
        "PITCHMIND_DEV_DEPLOY_SERVICE_CONNECTION": "${{ parameters.devDeploymentServiceConnection }}",
        "PITCHMIND_REGISTRY_NAME": "${{ parameters.registryName }}",
        "PITCHMIND_IMAGE_REPOSITORY": "pitchmind",
        "PITCHMIND_DEV_RESOURCE_GROUP": "${{ parameters.devResourceGroup }}",
        "PITCHMIND_DEV_APP_NAME": "${{ parameters.devAppName }}",
    }
    if not isinstance(environment, dict) or any(environment.get(key) != value for key, value in expected.items()):
        raise PipelinePolicyError("The identity preflight must receive the distinct connection parameters")
    if set(environment) != set(expected):
        raise PipelinePolicyError("The identity preflight environment may contain only the two reviewed identities")


def _require_release_stage_shape(stage: dict[str, Any], *, expected_jobs: tuple[str, ...]) -> None:
    """Reject unreviewed jobs or steps that could run around the approval chain."""
    jobs = _sequence(stage.get("jobs"), f"{stage.get('stage')} jobs")
    if len(jobs) != len(expected_jobs) or any(not isinstance(job, dict) for job in jobs):
        raise PipelinePolicyError(f"{stage.get('stage')} may contain only its reviewed jobs")
    actual_names = tuple(job.get("job", job.get("deployment")) for job in jobs)
    if actual_names != expected_jobs:
        raise PipelinePolicyError(f"{stage.get('stage')} may contain only its reviewed jobs")

    stage_name = stage.get("stage")
    if set(stage) != {"stage", "dependsOn", "condition", "jobs"}:
        raise PipelinePolicyError(f"{stage_name} may contain only reviewed stage fields")
    if stage_name == "OwnerApprovalBeforeAzureMutation":
        # The approval stage has a narrowly-scoped preflight followed by one
        # agentless gate. Their detailed contents are checked separately.
        return
    if stage_name == "OwnerApprovalOfReleaseDigest":
        return
    if stage_name == "BuildRelease":
        job = jobs[0]
        if set(job) != {"job", "steps"}:
            raise PipelinePolicyError("BuildRelease image job may contain only its reviewed fields")
        steps = _sequence(job.get("steps"), "BuildRelease image steps")
        if len(steps) != 2 or steps[0] != {"checkout": "self"}:
            raise PipelinePolicyError("BuildRelease may contain only checkout and the reviewed image-build template")
        if not isinstance(steps[1], dict) or steps[1].get("template") != "templates/build-image.yml":
            raise PipelinePolicyError("BuildRelease may contain only the reviewed image-build template")
        if set(steps[1]) != {"template", "parameters"}:
            raise PipelinePolicyError("BuildRelease image template call may not contain unreviewed fields")
        parameters = steps[1].get("parameters")
        if not isinstance(parameters, dict) or set(parameters) != {
            "acrPushServiceConnection", "registryName", "imageRepository"
        }:
            raise PipelinePolicyError("BuildRelease image template parameters must remain on the reviewed allowlist")
        return
    if stage_name == "DeployDev":
        deployment = jobs[0]
        if set(deployment) != {"deployment", "environment", "strategy"}:
            raise PipelinePolicyError("DeployDev job may contain only its reviewed fields")
        if deployment.get("environment") != "pitchmind-dev":
            raise PipelinePolicyError("DEV deployment must target the protected pitchmind-dev environment")
        strategy = deployment.get("strategy")
        if not isinstance(strategy, dict) or set(strategy) != {"runOnce"}:
            raise PipelinePolicyError("DeployDev strategy may contain only the reviewed runOnce strategy")
        run_once = strategy.get("runOnce") if isinstance(strategy, dict) else None
        if not isinstance(run_once, dict) or set(run_once) != {"deploy"}:
            raise PipelinePolicyError("DeployDev may not add unreviewed deployment lifecycle hooks")
        deploy = run_once.get("deploy") if isinstance(run_once, dict) else None
        if not isinstance(deploy, dict) or set(deploy) != {"steps"}:
            raise PipelinePolicyError("DeployDev deployment may contain only the reviewed steps")
        steps = deploy.get("steps") if isinstance(deploy, dict) else None
        if not isinstance(steps, list) or len(steps) != 2 or steps[0] != {"checkout": "self"}:
            raise PipelinePolicyError("DeployDev may contain only checkout and the reviewed deployment template")
        if not isinstance(steps[1], dict) or steps[1].get("template") != "templates/deploy-image.yml":
            raise PipelinePolicyError("DeployDev may contain only the reviewed deployment template")
        if set(steps[1]) != {"template", "parameters"}:
            raise PipelinePolicyError("DeployDev template call may not contain unreviewed fields")
        parameters = steps[1].get("parameters")
        if not isinstance(parameters, dict) or set(parameters) != {
            "devDeploymentServiceConnection", "resourceGroup", "appName"
        }:
            raise PipelinePolicyError("DeployDev template parameters must remain on the reviewed allowlist")


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
