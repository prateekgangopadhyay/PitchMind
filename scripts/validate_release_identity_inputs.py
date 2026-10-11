#!/usr/bin/env python3
"""Validate that release stages are configured with separate Azure identities."""
from __future__ import annotations

import os
import re
import sys


class IdentityInputError(ValueError):
    pass


_SERVICE_CONNECTION_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_. -]{0,127}$")
_JWT = re.compile(r"^[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}$")
_SECRET_PREFIX = re.compile(
    r"(?i)(?:bearer\s+|client[_-]?secret\s*[=:]|password\s*[=:]|"
    r"gh[pousr]_|github_pat_|eyJ[A-Za-z0-9_-]{20,})"
)
_ACR_NAME = re.compile(r"^[a-z0-9]{5,50}$")
_IMAGE_REPOSITORY = re.compile(r"^[a-z0-9]+(?:[._/-][a-z0-9]+)*$")
_RESOURCE_GROUP = re.compile(r"^[A-Za-z0-9._()-]{1,90}$")
_CONTAINER_APP = re.compile(r"^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])$")


def _validate_connection_name(value: str | None) -> str:
    if not isinstance(value, str):
        raise IdentityInputError("Both release service connections must be configured")
    normalized = value.strip()
    if not normalized or normalized.casefold() == "unconfigured":
        raise IdentityInputError("Both release service connections must be configured")
    if _SECRET_PREFIX.search(normalized) or _JWT.fullmatch(normalized):
        raise IdentityInputError("Service connection inputs must be names, not secret-shaped credentials")
    if not _SERVICE_CONNECTION_NAME.fullmatch(normalized):
        raise IdentityInputError("Service connection input contains characters invalid in a connection name")
    return normalized


def validate(acr_push: str | None, dev_deploy: str | None) -> None:
    acr_name = _validate_connection_name(acr_push)
    deploy_name = _validate_connection_name(dev_deploy)
    if acr_name.casefold() == deploy_name.casefold():
        raise IdentityInputError("Image-push and DEV-deployment service connections must be distinct")


def _validate_resource_input(value: str | None, *, label: str, pattern: re.Pattern[str], max_length: int) -> str:
    if not isinstance(value, str) or not value or len(value) > max_length or not pattern.fullmatch(value):
        raise IdentityInputError(f"{label} contains characters or a length invalid for its Azure resource name")
    if label == "Resource group" and value.endswith("."):
        raise IdentityInputError("Resource group must not end with a period")
    return value


def validate_release_resource_inputs(
    registry_name: str | None,
    image_repository: str | None,
    resource_group: str | None,
    app_name: str | None,
) -> None:
    _validate_resource_input(registry_name, label="Registry name", pattern=_ACR_NAME, max_length=50)
    _validate_resource_input(image_repository, label="Image repository", pattern=_IMAGE_REPOSITORY, max_length=255)
    _validate_resource_input(resource_group, label="Resource group", pattern=_RESOURCE_GROUP, max_length=90)
    _validate_resource_input(app_name, label="Container app name", pattern=_CONTAINER_APP, max_length=32)


def main() -> int:
    try:
        validate(os.environ.get("PITCHMIND_ACR_PUSH_SERVICE_CONNECTION"),
                 os.environ.get("PITCHMIND_DEV_DEPLOY_SERVICE_CONNECTION"))
        validate_release_resource_inputs(
            os.environ.get("PITCHMIND_REGISTRY_NAME"),
            os.environ.get("PITCHMIND_IMAGE_REPOSITORY"),
            os.environ.get("PITCHMIND_DEV_RESOURCE_GROUP"),
            os.environ.get("PITCHMIND_DEV_APP_NAME"),
        )
    except IdentityInputError as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    print("PASS: distinct service connections and safe release resource names are configured")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
