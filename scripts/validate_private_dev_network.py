#!/usr/bin/env python3
"""Fail-closed preflight for the existing VNet and private DEV subnet plan."""
from __future__ import annotations

import ipaddress
import json
import os
import sys


def reject_subnet_mutation_with_attached_controls(subnet: dict, label: str) -> None:
    for field in (
        "networkSecurityGroup", "routeTable", "serviceEndpoints", "serviceEndpointPolicies",
        "ipConfigurations", "natGateway", "resourceNavigationLinks", "sharingScope",
    ):
        if subnet.get(field):
            raise ValueError(f"{label} subnet has {field}; preserve/review it before IaC changes")


def main() -> int:
    try:
        vnet = json.loads(os.environ["PITCHMIND_DEV_VNET_JSON"])
        subnets = json.loads(os.environ["PITCHMIND_DEV_SUBNETS_JSON"])
        if str(vnet.get("location", "")).casefold() != os.environ["PITCHMIND_DEV_LOCATION"].casefold():
            raise ValueError("The existing VNet and target resource group must be in the same Azure region")
        aca_name = os.environ["PITCHMIND_DEV_ACA_SUBNET_NAME"]
        private_endpoint_name = os.environ["PITCHMIND_DEV_REDIS_PE_SUBNET_NAME"]
        aca_cidr = ipaddress.ip_network(os.environ["PITCHMIND_DEV_ACA_SUBNET_CIDR"], strict=True)
        pg_name = os.environ["PITCHMIND_DEV_POSTGRES_SUBNET_NAME"]
        pg_cidr = ipaddress.ip_network(os.environ["PITCHMIND_DEV_POSTGRES_SUBNET_CIDR"], strict=True)
        vnet_ranges = [ipaddress.ip_network(item) for item in vnet["addressSpace"]["addressPrefixes"]]
        if aca_cidr.version != 4 or aca_cidr.prefixlen > 27:
            raise ValueError("The ACA workload-profiles subnet must be IPv4 /27 or larger")
        if pg_cidr.version != 4 or pg_cidr.prefixlen > 28:
            raise ValueError("The PostgreSQL subnet must be IPv4 /28 or larger")
        if not any(aca_cidr.subnet_of(prefix) for prefix in vnet_ranges):
            raise ValueError("The ACA CIDR is outside the selected VNet address space")
        if not any(pg_cidr.subnet_of(prefix) for prefix in vnet_ranges):
            raise ValueError("The PostgreSQL CIDR is outside the selected VNet address space")
        if aca_cidr.overlaps(pg_cidr):
            raise ValueError("The ACA and PostgreSQL subnets overlap")

        by_name = {subnet["name"]: subnet for subnet in subnets}
        pg_subnet = by_name.get(pg_name)
        if pg_subnet is None:
            raise ValueError(f"PostgreSQL subnet {pg_name!r} does not exist")
        actual_pg = [ipaddress.ip_network(item) for item in pg_subnet.get("addressPrefixes", [])]
        if not actual_pg:
            actual_pg = [ipaddress.ip_network(pg_subnet["addressPrefix"])]
        if pg_cidr not in actual_pg:
            raise ValueError("The PostgreSQL CIDR parameter does not match the existing subnet")
        pg_delegations = {
            item.get("serviceName")
            for item in pg_subnet.get("delegations", [])
        }
        if pg_delegations - {"Microsoft.DBforPostgreSQL/flexibleServers"}:
            raise ValueError("The PostgreSQL subnet is delegated to a different service")
        if os.environ.get("PITCHMIND_DEV_PROVISION_POSTGRES", "false").lower() == "true":
            # The template changes this existing subnet to add PostgreSQL
            # delegation. Fail closed rather than clearing attached controls.
            reject_subnet_mutation_with_attached_controls(pg_subnet, "PostgreSQL")

        aca_subnet = by_name.get(aca_name)
        for subnet in subnets:
            if subnet["name"] in {aca_name, pg_name}:
                continue
            prefixes = subnet.get("addressPrefixes", []) or [subnet.get("addressPrefix")]
            for prefix in filter(None, prefixes):
                if aca_cidr.overlaps(ipaddress.ip_network(prefix)):
                    raise ValueError(f"The ACA CIDR overlaps existing subnet {subnet['name']!r}")
        if aca_subnet:
            actual_aca = [ipaddress.ip_network(item) for item in aca_subnet.get("addressPrefixes", [])]
            if not actual_aca:
                actual_aca = [ipaddress.ip_network(aca_subnet["addressPrefix"])]
            if aca_cidr not in actual_aca:
                raise ValueError("The ACA CIDR differs from the existing container-apps subnet")
            aca_delegations = {
                item.get("serviceName")
                for item in aca_subnet.get("delegations", [])
            }
            if aca_delegations - {"Microsoft.App/environments"}:
                raise ValueError("The ACA subnet is delegated to a different service")
            reject_subnet_mutation_with_attached_controls(aca_subnet, "ACA")

        if os.environ.get("PITCHMIND_DEV_PROVISION_REDIS", "false").lower() == "true":
            endpoint_subnet = by_name.get(private_endpoint_name)
            if endpoint_subnet is None:
                raise ValueError(f"Redis private endpoint subnet {private_endpoint_name!r} does not exist")
            if endpoint_subnet.get("delegations"):
                raise ValueError("Redis private endpoint subnet must not be delegated")

        print("PASS: VNet and subnet ranges are non-overlapping and target delegations are safe")
        return 0
    except (KeyError, ValueError, TypeError, json.JSONDecodeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
