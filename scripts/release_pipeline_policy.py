"""Structural checks for the opt-in Azure release path.

These checks validate parsed repository YAML only. They do not prove Azure
DevOps federation, RBAC, protected-environment configuration, or owner approval.
"""
from __future__ import annotations

import hashlib
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
    "Cache@2",
    "PublishTestResults@2",
}
_AZ_COMMAND = re.compile(r"\baz\s+([a-z][a-z0-9-]*)\s*([a-z][a-z0-9-]*)?", re.IGNORECASE)
_AZ_MUTATING_CMDLETS = re.compile(r"\b[A-Za-z][A-Za-z0-9]*-Az[A-Za-z0-9]+\b")
_OTHER_MUTATORS = re.compile(
    r"\b(?:terraform\s+(?:apply|destroy)|bicep\s+deploy|kubectl\s+apply|azd\s+(?:provision|deploy))\b",
    re.IGNORECASE,
)
_ALLOWED_AZ_BICEP = {"version", "build", "build-params"}
# Azure Pipelines accepts shortcut steps such as `bash:` in addition to
# explicit `task:` mappings. Pin every reviewed validation shortcut so an
# unreviewed executable step cannot hide behind a harmless-looking mapping.
_ALLOWED_VALIDATION_SHORTCUTS = {
    "Validate repository and Compose configuration": (
        None,
        "5ae8a20741be611bcb86e1aa5825ecf6b665ca472498b7d7476e86d949983d41",
    ),
    "Verify source-feed preflight and planning artifacts": (
        None,
        "301ced44d3e2a346afab9e9011543d8c3e4de480e6cb71b83695b2d7a29634df",
    ),
    "Docker Compose health and real PostgreSQL integration": (
        None,
        "382734eb1d0ad67f555626c142f50de3cdf304f3be4af1db147014fc619d2c0f",
    ),
    "Restore, build and test solution": (
        None,
        "8e625219cf0d9e77190ff1ae79509b362644a20a503a3dbd55c6102e1212fa40",
    ),
    "Frontend install, build, test and lint": (
        "frontend",
        "d7973f8d2cab09c731b4c73c0a6460517fb0e8e49e878fb03624006093d52d69",
    ),
    "Compile DEV/STAGE/PROD Bicep and check environment isolation (no deployment)": (
        None,
        "5ecd9946d83c321927d619bc94219d161dfe1aba74a4b005f4528bd8246d8934",
    ),
    "Run the deliberately failing test; its nonzero exit must fail Validate": (
        None,
        "d2b812706e73c2df12187fb4af49277259a7b33062b5aedcf01e3f1c0d3cab8e",
    ),
}
# Script changes in AzureCLI release templates require this policy pin to be
# reviewed alongside a command-level diff and the mutation tests.
_BUILD_SCRIPT_SHA256 = "9f206c7c4b7aa95ec0f138bbc111dc3bc4b9c65e54b6970daa58c44f4188cc98"
_DEPLOY_SCRIPT_SHA256 = "bf15c174c0a2f5cd7665b08a8c835d2b453bbb578fe2443be2aa7c4bfd64b773"
_IDENTITY_VALIDATOR_SHA256 = "e26f4f763c3ec05a0c10b00827e90e1fdcbbae9bf24c73b37cbffc294a227262"
_HTTP_SMOKE_SCRIPT_SHA256 = "c3d37212570bba7ee89c2e5752d3257c8ff35ddceaa48d3820e288495fff3b1b"
_PRIVATE_DEV_SOURCE_HASHES: dict[str, str] = {
    "scripts/private-dev-plan.sh": "1023285f4dbda3a7054ad78517132db2a092b8605e43b25d61e91027cdcc5b5f",
    "scripts/private-dev-deploy.sh": "c2efe76b3466c1e4e147c1c65ee7ec85c28d125319584c3fa47242a90db3394c",
    "scripts/validate_private_dev_network.py": "adec0a92b270b140cff4a1ab1aa0bedabb9288e086dca55ba42265c3a60d1121",
    "scripts/validate_private_dev_whatif.py": "9af6df49d622bd094fa81b71ef0b0bc005934bfd26714243bc1b63fd59cce32f",
    "pipelines/templates/preview-private-dev.yml": "c4cd3b3355194aedafa4b529b58b3132d5b92263e78fa7cafa49da56b3fce971",
    "pipelines/templates/provision-private-dev.yml": "642cc09a646e8cd7bd642dcf94df46470dda402c9f2425332d3d26d789ef6b6f",
    "infra/private-dev-topology.bicep": "6f0768aabc0c56613dabd0e2de75a5c56977e0ab574274e24f35e8fd75715093",
}


def validate_pipeline_policy(
    pipeline: str,
    build_template: str,
    deploy_template: str,
    validation_templates: dict[str, str],
    release_helpers: dict[str, str],
) -> None:
    """Reject policy weakening by inspecting active YAML mappings and steps."""
    pipeline_doc = _load_yaml(pipeline, "pipeline")
    build_doc = _load_yaml(build_template, "image build template")
    deploy_doc = _load_yaml(deploy_template, "deployment template")

    pipeline_pool = pipeline_doc.get("pool")
    if not isinstance(pipeline_pool, dict) or pipeline_pool != {"name": "Default"}:
        raise PipelinePolicyError("Pipeline jobs must use the self-hosted Default agent pool")
    _validate_job_pools(pipeline_doc)

    parameters = _sequence(pipeline_doc.get("parameters"), "pipeline parameters")
    probe_parameter = next(
        (item for item in parameters if isinstance(item, dict) and item.get("name") == "runGateFailureProbe"),
        None,
    )
    if probe_parameter is not None and (probe_parameter.get("type") != "boolean" or probe_parameter.get("default") is not False):
        raise PipelinePolicyError("Gate failure probe must remain a boolean default-off parameter")

    delivery_parameter = next(
        (item for item in parameters if isinstance(item, dict) and item.get("name") == "enableDevDelivery"),
        None,
    )
    if delivery_parameter is None or delivery_parameter.get("type") != "boolean":
        raise PipelinePolicyError("DEV delivery must remain a boolean parameter")
    for parameter_name in ("provisionPostgres", "provisionRedis"):
        option = next(
            (item for item in parameters if isinstance(item, dict) and item.get("name") == parameter_name),
            None,
        )
        if option is None or option.get("type") != "boolean" or option.get("default") is not False:
            raise PipelinePolicyError(f"{parameter_name} must remain a boolean default-off resource option")
    candidate_feed_option = next(
        (item for item in parameters if isinstance(item, dict) and item.get("name") == "enableCandidateFeed"),
        None,
    )
    if candidate_feed_option is None or candidate_feed_option.get("type") != "boolean" or candidate_feed_option.get("default") is not True:
        raise PipelinePolicyError("enableCandidateFeed must remain a boolean default-on deployment option")
    for parameter_name, allowed_defaults in (
        ("devInfrastructureServiceConnection", ("unconfigured", "pitchmind-dev-infrastructure-wif")),
        ("devAcaSubnetCidr", ("", "10.42.0.0/27")),
        ("devEstimatedMonthlyCost", ("", "$15")),
    ):
        option = next(
            (item for item in parameters if isinstance(item, dict) and item.get("name") == parameter_name),
            None,
        )
        if option is None or option.get("type") != "string" or option.get("default") not in allowed_defaults:
            raise PipelinePolicyError(f"{parameter_name} must remain an explicit, reviewed string input")
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

    pre_mutation_jobs = tuple(
        job.get("job") for job in _sequence(stages_by_name["OwnerApprovalBeforeAzureMutation"].get("jobs"), "pre-mutation jobs")
        if isinstance(job, dict)
    )
    if pre_mutation_jobs == ("ValidateReleaseIdentityInputs", "AwaitExplicitOwnerApproval"):
        _require_manual_approval(
            stages_by_name["OwnerApprovalBeforeAzureMutation"],
            job_name="AwaitExplicitOwnerApproval",
            depends_on="ValidateReleaseIdentityInputs",
            instruction_tokens=("explicit repository-owner approval", "this story/change set"),
        )
    elif pre_mutation_jobs != ("ValidateReleaseIdentityInputs",):
        raise PipelinePolicyError("OwnerApprovalBeforeAzureMutation may contain only ValidateReleaseIdentityInputs and optional AwaitExplicitOwnerApproval")

    _require_identity_preflight(stages_by_name["OwnerApprovalBeforeAzureMutation"])
    _require_release_stage_shape(
        stages_by_name["OwnerApprovalBeforeAzureMutation"],
        expected_jobs=pre_mutation_jobs,
    )

    digest_jobs = tuple(
        job.get("job") for job in _sequence(digest_approval.get("jobs"), "digest approval jobs")
        if isinstance(job, dict)
    )
    if digest_jobs == ("PreviewPrivateDevTopology", "AwaitDigestConfirmation"):
        _require_manual_approval(
            digest_approval,
            job_name="AwaitDigestConfirmation",
            depends_on="PreviewPrivateDevTopology",
            instruction_tokens=("release manifest image digest", "explicit owner approval"),
        )
    elif digest_jobs != ("PreviewPrivateDevTopology",):
        raise PipelinePolicyError("OwnerApprovalOfReleaseDigest may contain only PreviewPrivateDevTopology and optional AwaitDigestConfirmation")

    _require_release_stage_shape(
        digest_approval,
        expected_jobs=digest_jobs,
    )
    _require_private_dev_preview(digest_approval)

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
    preview_call = _find_one_template_call(digest_approval, "templates/preview-private-dev.yml")
    provision_call = _find_one_template_call(deploy_stage, "templates/provision-private-dev.yml")
    if build_call.get("parameters", {}).get("acrPushServiceConnection") != "${{ parameters.acrPushServiceConnection }}":
        raise PipelinePolicyError("BuildRelease must pass the dedicated ACR-push service connection")
    if deploy_call.get("parameters", {}).get("devDeploymentServiceConnection") != "${{ parameters.devDeploymentServiceConnection }}":
        raise PipelinePolicyError("DeployDev must pass the dedicated DEV deployment service connection")
    expected_private_parameters = {
        "devInfrastructureServiceConnection": "${{ parameters.devInfrastructureServiceConnection }}",
        "registryName": "${{ parameters.registryName }}",
        "resourceGroup": "${{ parameters.devResourceGroup }}",
        "vnetName": "${{ parameters.devVnetName }}",
        "acaSubnetName": "${{ parameters.devAcaSubnetName }}",
        "acaSubnetCidr": "${{ parameters.devAcaSubnetCidr }}",
        "postgresSubnetName": "${{ parameters.devPostgresSubnetName }}",
        "postgresSubnetCidr": "${{ parameters.devPostgresSubnetCidr }}",
        "provisionRedis": "${{ parameters.provisionRedis }}",
        "enableCandidateFeed": "${{ parameters.enableCandidateFeed }}",
        "signalRName": "${{ parameters.devSignalRName }}",
        "signalRAllowedOrigin": "${{ parameters.devPublicAppOrigin }}",
        "storageAccountName": "${{ parameters.devStorageAccountName }}",
        "candidateCheckpointContainer": "${{ parameters.devCandidateCheckpointContainer }}",
        "redisName": "${{ parameters.devRedisName }}",
        "redisSkuName": "${{ parameters.devRedisSkuName }}",
        "redisPrivateEndpointSubnetName": "${{ parameters.devRedisPrivateEndpointSubnetName }}",
        "redisPrivateDnsZoneName": "${{ parameters.devRedisPrivateDnsZoneName }}",
        "redisPrivateEndpointName": "${{ parameters.devRedisPrivateEndpointName }}",
        "postgresDnsZoneName": "${{ parameters.devPostgresDnsZoneName }}",
        "appName": "${{ parameters.devAppName }}",
        "environmentName": "${{ parameters.devEnvironmentName }}",
        "provisionPostgres": "${{ parameters.provisionPostgres }}",
        "postgresServerName": "${{ parameters.devPostgresServerName }}",
        "postgresSkuTier": "${{ parameters.devPostgresSkuTier }}",
        "postgresSkuName": "${{ parameters.devPostgresSkuName }}",
        "postgresAdminObjectId": "${{ parameters.devPostgresAdminObjectId }}",
        "postgresAdminPrincipalName": "${{ parameters.devPostgresAdminPrincipalName }}",
        "postgresAdminPrincipalType": "${{ parameters.devPostgresAdminPrincipalType }}",
        "estimatedMonthlyCost": "${{ parameters.devEstimatedMonthlyCost }}",
    }
    if preview_call.get("parameters") != expected_private_parameters:
        raise PipelinePolicyError("The private DEV preview must receive the exact reviewed pipeline parameters")
    expected_apply_parameters = {
        key: value for key, value in expected_private_parameters.items()
        if key != "estimatedMonthlyCost"
    }
    if provision_call.get("parameters") != expected_apply_parameters:
        raise PipelinePolicyError("The private DEV apply must use the same reviewed target as its what-if")

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
    _assert_release_helpers(release_helpers)
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
        ("acrPushServiceConnection", "registryName", "imageRepository", "entraTenantId", "entraClientId", "entraApiScope")
        if kind == "build" else
        ("devDeploymentServiceConnection", "resourceGroup", "appName")
    )
    expected_parameters = (
        [
            {"name": "acrPushServiceConnection", "type": "string"},
            {"name": "registryName", "type": "string"},
            {"name": "imageRepository", "type": "string", "default": "pitchmind"},
            {"name": "entraTenantId", "type": "string"},
            {"name": "entraClientId", "type": "string"},
            {"name": "entraApiScope", "type": "string"},
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

    expected_step_count = 2 if kind == "build" else 3
    if not isinstance(steps, list) or len(steps) != expected_step_count:
        raise PipelinePolicyError(f"The {kind} release template may contain only its reviewed steps")
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
        smoke_step = steps[2]
        if smoke_step != {
            "task": "Bash@3",
            "displayName": "Smoke deployed DEV endpoint outside the Azure CLI task",
            "env": {"PITCHMIND_DEV_FQDN": "$(PITCHMIND_DEV_FQDN)"},
            "inputs": {
                "targetType": "inline",
            "script": 'python3 scripts/api-smoke.py --expect-auth-required "https://$PITCHMIND_DEV_FQDN"',
            },
        }:
            raise PipelinePolicyError("The DEV endpoint smoke must run only in its reviewed post-deploy Bash step")
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


def _assert_release_helpers(release_helpers: dict[str, str]) -> None:
    expected_hashes = {
        "scripts/validate_release_identity_inputs.py": _IDENTITY_VALIDATOR_SHA256,
        "scripts/api-smoke.py": _HTTP_SMOKE_SCRIPT_SHA256,
        **_PRIVATE_DEV_SOURCE_HASHES,
    }
    if set(release_helpers) != set(expected_hashes):
        raise PipelinePolicyError("The release helper source set must remain on the reviewed allowlist")
    for helper_path, expected_hash in expected_hashes.items():
        actual_hash = hashlib.sha256(release_helpers[helper_path].encode("utf-8")).hexdigest()
        if actual_hash != expected_hash:
            raise PipelinePolicyError(f"Release helper {helper_path} changed without a reviewed policy pin")


def _require_private_dev_preview(stage: dict[str, Any]) -> None:
    jobs = _sequence(stage.get("jobs"), "digest approval jobs")
    if not jobs or not isinstance(jobs[0], dict):
        raise PipelinePolicyError("Digest approval must run the private DEV what-if before the gate")
    job = jobs[0]
    if set(job) != {"job", "displayName", "steps"} or job.get("job") != "PreviewPrivateDevTopology":
        raise PipelinePolicyError("Digest approval must begin with the reviewed private DEV preview job")
    steps = _sequence(job.get("steps"), "private DEV preview steps")
    if len(steps) != 2 or steps[0] != {"checkout": "self"}:
        raise PipelinePolicyError("Private DEV preview may contain only checkout and its reviewed template")
    if not isinstance(steps[1], dict) or steps[1].get("template") != "templates/preview-private-dev.yml" or \
            set(steps[1]) != {"template", "parameters"}:
        raise PipelinePolicyError("Private DEV preview must call only the reviewed what-if template")


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
        raise PipelinePolicyError("Pre-mutation stage must include the release identity-input preflight")
    if set(matching_jobs[0]) != {"job", "steps"}:
        raise PipelinePolicyError("Identity preflight job may contain only its reviewed fields")
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
        "PITCHMIND_DEV_INFRA_SERVICE_CONNECTION": "${{ parameters.devInfrastructureServiceConnection }}",
        "PITCHMIND_REGISTRY_NAME": "${{ parameters.registryName }}",
        "PITCHMIND_IMAGE_REPOSITORY": "pitchmind",
        "PITCHMIND_DEV_RESOURCE_GROUP": "${{ parameters.devResourceGroup }}",
        "PITCHMIND_DEV_APP_NAME": "${{ parameters.devAppName }}",
        "PITCHMIND_DEV_VNET_NAME": "${{ parameters.devVnetName }}",
        "PITCHMIND_DEV_ACA_SUBNET_NAME": "${{ parameters.devAcaSubnetName }}",
        "PITCHMIND_DEV_POSTGRES_SUBNET_NAME": "${{ parameters.devPostgresSubnetName }}",
        "PITCHMIND_DEV_REDIS_PE_SUBNET_NAME": "${{ parameters.devRedisPrivateEndpointSubnetName }}",
        "PITCHMIND_DEV_ACA_SUBNET_CIDR": "${{ parameters.devAcaSubnetCidr }}",
        "PITCHMIND_DEV_ESTIMATED_MONTHLY_COST": "${{ parameters.devEstimatedMonthlyCost }}",
    }
    if not isinstance(environment, dict) or any(environment.get(key) != value for key, value in expected.items()):
        raise PipelinePolicyError("The identity preflight must receive the distinct connection parameters")
    if set(environment) != set(expected):
        raise PipelinePolicyError("The identity preflight environment may contain only the reviewed release inputs")


def _validate_job_pools(node: Any) -> None:
    """Reject job-level pools that bypass the configured self-hosted pool."""
    if isinstance(node, dict):
        if "job" in node or "deployment" in node:
            pool = node.get("pool")
            if pool is not None and pool != "server" and pool != {"name": "Default"}:
                raise PipelinePolicyError("Agent jobs must inherit the self-hosted Default pool")
        for value in node.values():
            _validate_job_pools(value)
    elif isinstance(node, list):
        for value in node:
            _validate_job_pools(value)


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
        if len(jobs) not in (1, 2) or jobs[0].get("job") != "PreviewPrivateDevTopology":
            raise PipelinePolicyError("Digest approval must preview first with PreviewPrivateDevTopology")
        if len(jobs) == 2 and jobs[1].get("job") != "AwaitDigestConfirmation":
            raise PipelinePolicyError("Digest approval secondary job must be AwaitDigestConfirmation")
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
            "acrPushServiceConnection", "registryName", "imageRepository", "entraTenantId", "entraClientId", "entraApiScope"
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
        if not isinstance(steps, list) or len(steps) != 3 or steps[0] != {"checkout": "self"}:
            raise PipelinePolicyError("DeployDev may contain only checkout, approved topology apply and image deployment")
        if not isinstance(steps[1], dict) or steps[1].get("template") != "templates/provision-private-dev.yml" or \
                not isinstance(steps[2], dict) or steps[2].get("template") != "templates/deploy-image.yml":
            raise PipelinePolicyError("DeployDev must apply the reviewed topology before the reviewed image deployment")
        if set(steps[1]) != {"template", "parameters"} or set(steps[2]) != {"template", "parameters"}:
            raise PipelinePolicyError("DeployDev template call may not contain unreviewed fields")
        parameters = steps[2].get("parameters")
        provision_parameters = steps[1].get("parameters")
        if not isinstance(parameters, dict) or set(parameters) != {
            "devDeploymentServiceConnection", "resourceGroup", "appName"
        }:
            raise PipelinePolicyError("DeployDev template parameters must remain on the reviewed allowlist")
        if not isinstance(provision_parameters, dict) or "estimatedMonthlyCost" in provision_parameters:
            raise PipelinePolicyError("DeployDev topology apply parameters must remain on the reviewed allowlist")


def _assert_validation_step_shapes(node: Any, label: str) -> None:
    """Allow only reviewed validation step forms and pinned shortcut scripts."""
    def validate_steps(value: Any) -> None:
        if isinstance(value, dict):
            if "steps" in value:
                steps = value["steps"]
                if not isinstance(steps, list):
                    raise PipelinePolicyError(f"Validation steps must be a sequence in {label}")
                for step in steps:
                    _assert_validation_step(step, label)
            for child in value.values():
                validate_steps(child)
        elif isinstance(value, list):
            for child in value:
                validate_steps(child)

    validate_steps(node)


def _assert_validation_step(step: Any, label: str) -> None:
    if not isinstance(step, dict):
        raise PipelinePolicyError(f"Unreviewed validation step shape in {label}")
    if set(step) == {"checkout"} and step["checkout"] == "self":
        return
    if set(step) == {"template"} and step["template"] in _VALIDATION_TEMPLATES:
        return
    if "bash" in step:
        if set(step) not in ({"bash", "displayName"}, {"bash", "displayName", "workingDirectory"}, {"bash", "displayName", "env"}):
            raise PipelinePolicyError(f"Unreviewed Bash shortcut fields are not allowed in {label}")
        if "env" in step and step.get("env") != {"PITCHMIND_GATE_FAILURE_PROBE": "1"}:
            raise PipelinePolicyError(f"Unreviewed Bash shortcut env mapping in {label}")
        display_name = step.get("displayName")
        reviewed = _ALLOWED_VALIDATION_SHORTCUTS.get(display_name)
        if reviewed is None:
            raise PipelinePolicyError(f"Unreviewed Bash shortcut is not allowed in {label}")
        expected_directory, expected_hash = reviewed
        if step.get("workingDirectory") != expected_directory:
            raise PipelinePolicyError(f"Bash shortcut working directory changed in {label}")
        script = step.get("bash")
        if not isinstance(script, str) or hashlib.sha256(script.encode("utf-8")).hexdigest() != expected_hash:
            raise PipelinePolicyError(f"Bash shortcut script changed without a reviewed policy pin in {label}")
        return

    task_name = step.get("task")
    if task_name not in _ALLOWED_VALIDATION_TASKS:
        raise PipelinePolicyError(f"Unreviewed validation step shape in {label}")
    task_fields = {
        "UseDotNet@2": frozenset({"task", "inputs"}),
        "NodeTool@0": frozenset({"task", "inputs"}),
        "Cache@2": frozenset({"task", "inputs"}),
        "PublishTestResults@2": frozenset({"task", "inputs"}),
    }
    base_fields = task_fields[task_name]
    allowed_fields = {base_fields, base_fields | {"displayName"}}
    if task_name == "PublishTestResults@2":
        allowed_fields |= {fields | {"condition"} for fields in tuple(allowed_fields)}
    if frozenset(step) not in allowed_fields:
        raise PipelinePolicyError(f"Unreviewed {task_name} step fields are not allowed in {label}")
    inputs = step.get("inputs")
    if not isinstance(inputs, dict) or ("displayName" in step and not isinstance(step["displayName"], str)):
        raise PipelinePolicyError(f"{task_name} must have reviewed display and input mappings in {label}")
    if task_name == "UseDotNet@2" and inputs != {"packageType": "sdk", "version": "10.0.x"}:
        raise PipelinePolicyError(f"UseDotNet inputs changed without a policy review in {label}")
    if task_name == "NodeTool@0" and inputs != {"versionSpec": "22.x"}:
        raise PipelinePolicyError(f"NodeTool inputs changed without a policy review in {label}")
    if task_name == "Cache@2":
        approved_caches = {
            (
                '"nuget" | "$(Agent.OS)" | **/packages.lock.json',
                '"nuget" | "$(Agent.OS)"\n',
                "$(Pipeline.Workspace)/.nuget/packages",
            ),
            (
                '"npm" | "$(Agent.OS)" | frontend/package-lock.json',
                '"npm" | "$(Agent.OS)"\n',
                "$(Pipeline.Workspace)/.npm",
            ),
            (
                '"pip" | "$(Agent.OS)" | scripts/requirements-policy.txt',
                '"pip" | "$(Agent.OS)"\n',
                "$(Pipeline.Workspace)/.cache/pip",
            ),
        }
        cache = (
            inputs.get("key"),
            inputs.get("restoreKeys"),
            inputs.get("path"),
        ) if set(inputs) == {"key", "restoreKeys", "path"} else None
        if cache not in approved_caches:
            raise PipelinePolicyError(f"Cache@2 key, restore keys, or path changed without a policy review in {label}")
    if task_name == "PublishTestResults@2":
        allowed_inputs = {
            "testResultsFormat",
            "testResultsFiles",
            "failTaskOnFailedTests",
            "failTaskOnMissingResultsFile",
        }
        if set(inputs) != allowed_inputs or inputs.get("testResultsFormat") not in {"VSTest", "JUnit"} or \
                not isinstance(inputs.get("testResultsFiles"), str) or inputs.get("failTaskOnFailedTests") is not True or \
                inputs.get("failTaskOnMissingResultsFile") is not True:
            raise PipelinePolicyError(f"PublishTestResults inputs changed without a policy review in {label}")
        if "condition" in step and step["condition"] != "succeededOrFailed()":
            raise PipelinePolicyError(f"PublishTestResults condition changed without a policy review in {label}")


def _assert_validation_only(node: Any, label: str) -> None:
    _assert_validation_step_shapes(node, label)
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
