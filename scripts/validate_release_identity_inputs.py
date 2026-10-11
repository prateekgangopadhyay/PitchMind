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


def main() -> int:
    try:
        validate(os.environ.get("PITCHMIND_ACR_PUSH_SERVICE_CONNECTION"),
                 os.environ.get("PITCHMIND_DEV_DEPLOY_SERVICE_CONNECTION"))
    except IdentityInputError as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    print("PASS: distinct image-push and DEV-deployment service connections are configured")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
