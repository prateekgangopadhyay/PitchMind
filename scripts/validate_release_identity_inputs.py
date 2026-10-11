#!/usr/bin/env python3
"""Validate that release stages are configured with separate Azure identities."""
from __future__ import annotations

import os
import sys


class IdentityInputError(ValueError):
    pass


def validate(acr_push: str | None, dev_deploy: str | None) -> None:
    values = (acr_push, dev_deploy)
    if any(not isinstance(value, str) or not value.strip() or value.strip().casefold() == "unconfigured"
           for value in values):
        raise IdentityInputError("Both release service connections must be configured")
    if acr_push.strip().casefold() == dev_deploy.strip().casefold():
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
