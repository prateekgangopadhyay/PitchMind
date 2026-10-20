#!/usr/bin/env python3
"""Reject delete or out-of-scope changes in the reviewed private DEV what-if."""
from __future__ import annotations

import json
import sys

ALLOWED_TYPES = {
    "microsoft.network/virtualnetworks/subnets",
    "microsoft.network/privatednszones",
    "microsoft.network/privatednszones/virtualnetworklinks",
    "microsoft.managedidentity/userassignedidentities",
    "microsoft.authorization/roleassignments",
    "microsoft.dbforpostgresql/flexibleservers",
    "microsoft.dbforpostgresql/flexibleservers/administrators",
    "microsoft.dbforpostgresql/flexibleservers/databases",
    "microsoft.cache/redisenterprise",
    "microsoft.cache/redisenterprise/databases",
    "microsoft.cache/redisenterprise/databases/accesspolicyassignments",
    "microsoft.network/privateendpoints",
    "microsoft.network/privateendpoints/privatednszonegroups",
    "microsoft.app/managedenvironments",
    "microsoft.app/containerapps",
    "microsoft.app/jobs",
    "microsoft.eventhub/namespaces/eventhubs",
    "microsoft.eventhub/namespaces/eventhubs/consumergroups",
    "microsoft.signalrservice/signalr",
    "microsoft.storage/storageaccounts/blobservices/containers",
}
TARGET_RESOURCE_GROUP = "rg-pitchmind-dev-001"
TARGET_RESOURCE_NAMES = {
    "pm-dev-vnet", "postgres", "container-apps", "private-endpoints",
    "pm-dev-private.postgres.database.azure.com", "pm-dev-private-cae-link",
    "pm-dev-private-cae-redis-link", "privatelink.redis.azure.net",
    "pm-dev-api-vnet", "pm-dev-api-vnet-pull", "pm-dev-api-vnet-runtime",
    "pm-dev-api-vnet-eventworker", "pm-dev-api-vnet-blob-link", "pm-dev-api-vnet-blob-pe",
    "pm-dev-private-cae-blob-link",
    "pm-dev-signalr", "pm-dev-blob-pe", "privatelink.blob.core.windows.net",
    "pmdev4iwntkatliy6sst", "candidate-checkpoints", "pitchmind-api-cursor-cache",
    "pm-dev-api-vnet-db-migrator", "pm-dev-private-cae",
    "pitchmind-postgresql-dev", "pitchmind", "pm-dev-redis", "default", "pm-dev-redis-pe",
}
TARGET_EVENTHUB_NAMESPACE = "pm-dev-eh"


def load_changes(path: str) -> list[dict]:
    with open(path, encoding="utf-8") as source:
        plan = json.load(source)
    changes = plan.get("changes")
    if changes is None and isinstance(plan.get("properties"), dict):
        changes = plan["properties"].get("changes")
    if not isinstance(changes, list):
        raise ValueError(f"Azure what-if response at {path} has no changes array")
    return changes


def main() -> int:
    try:
        if len(sys.argv) not in (2, 3):
            raise ValueError("Usage: validate_private_dev_whatif.py WHATIF.json [APPROVED_WHATIF.json]")
        changes = load_changes(sys.argv[1])
        for change in changes:
            change_type = str(change.get("changeType", "")).casefold()
            resource_type = str(
                change.get("resourceType")
                or (change.get("after") or change.get("before") or {}).get("type", "")
            ).casefold()
            if change_type == "delete":
                raise ValueError(f"Refusing a plan that deletes {change.get('resourceId', 'an Azure resource')}")
            if change_type not in {"create", "modify", "ignore", "nochange", "deploy"}:
                raise ValueError(f"Unrecognized what-if change type: {change_type!r}")
            if change_type not in {"ignore", "nochange"} and resource_type not in ALLOWED_TYPES:
                raise ValueError(f"Out-of-scope resource in plan: {resource_type or 'unknown'}")
            if change_type not in {"ignore", "nochange"}:
                resource_id = str(change.get("resourceId", "")).casefold()
                if f"/resourcegroups/{TARGET_RESOURCE_GROUP}/providers/" not in resource_id:
                    raise ValueError("Planned mutation is outside the approved DEV resource group")
                if resource_type == "microsoft.authorization/roleassignments":
                    approved_scopes = (
                        "/providers/microsoft.containerregistry/registries/pmdev4iwntkatliy6sacr/",
                        "/providers/microsoft.eventhub/namespaces/pm-dev-eh/eventhubs/candidate-match-events/",
                        "/providers/microsoft.storage/storageaccounts/pmdev4iwntkatliy6sst/blobservices/default/containers/candidate-checkpoints/",
                        "/providers/microsoft.signalrservice/signalr/pm-dev-signalr/",
                    )
                    if not any(scope in resource_id for scope in approved_scopes) or "/providers/microsoft.authorization/roleassignments/" not in resource_id:
                        raise ValueError("Role assignment is outside an approved ACR, candidate Event Hub, checkpoint container, or SignalR scope")
                elif resource_type in {
                    "microsoft.eventhub/namespaces/eventhubs",
                    "microsoft.eventhub/namespaces/eventhubs/consumergroups",
                }:
                    expected_eventhub_path = (
                        f"/providers/microsoft.eventhub/namespaces/{TARGET_EVENTHUB_NAMESPACE}"
                        "/eventhubs/candidate-match-events"
                    )
                    if expected_eventhub_path not in resource_id:
                        raise ValueError("Event Hubs mutation is outside the approved candidate hub")
                    if resource_type.endswith("/consumergroups") and not resource_id.endswith(
                        "/consumergroups/pitchmind-candidate-worker"
                    ):
                        raise ValueError("Consumer group mutation is outside the approved candidate group")
                elif resource_type.endswith("/subnets"):
                    if not any(f"/subnets/{name}" in resource_id for name in ("postgres", "container-apps")):
                        raise ValueError("Subnet mutation is outside the two approved service subnets")
                elif resource_type == "microsoft.cache/redisenterprise/databases/accesspolicyassignments":
                    approved_redis_assignment = "/providers/microsoft.cache/redisenterprise/pm-dev-redis/databases/default/accesspolicyassignments/pitchmind-api-cursor-cache"
                    if approved_redis_assignment not in resource_id:
                        raise ValueError("Redis access policy assignment is outside the approved DEV cache identity")
                elif not any(f"/{name}" in resource_id for name in TARGET_RESOURCE_NAMES):
                    raise ValueError("Planned mutation does not match an approved private DEV resource name")
        if len(sys.argv) == 3:
            import hashlib
            def signature(items: list[dict]) -> str:
                normalized = json.dumps(sorted(items, key=lambda item: str(item.get("resourceId", "")).casefold()),
                                        sort_keys=True, separators=(",", ":"))
                return hashlib.sha256(normalized.encode("utf-8")).hexdigest()
            approved = load_changes(sys.argv[2])
            if signature(changes) != signature(approved):
                raise ValueError("Current Azure what-if differs from the plan approved at the owner gate")
        suffix = " and still exactly matches the approved plan" if len(sys.argv) == 3 else ""
        print(f"PASS: Azure what-if contains {len(changes)} changes, no deletes/out-of-scope types{suffix}")
        return 0
    except (OSError, json.JSONDecodeError, KeyError, TypeError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
