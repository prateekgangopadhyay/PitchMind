from __future__ import annotations

import shutil
import tempfile
import unittest
from pathlib import Path

from scripts.validate_infrastructure_profiles import validate_profiles


REPO_ROOT = Path(__file__).resolve().parent.parent


class InfrastructureProfileValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        shutil.copytree(REPO_ROOT / "infra", self.root / "infra")

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_repository_profiles_are_synchronized_and_isolated(self) -> None:
        self.assertEqual([], validate_profiles(self.root))

    def test_stage_cannot_enable_dev_network_foundation(self) -> None:
        params = self.root / "infra/environments/stage.bicepparam"
        params.write_text(params.read_text(encoding="utf-8").replace(
            "param enableDevFoundation = false", "param enableDevFoundation = true"), encoding="utf-8")

        errors = validate_profiles(self.root)
        self.assertTrue(any("stage" in error and "enableDevFoundation=false" in error for error in errors))
        self.assertTrue(any("stage profile disagrees" in error for error in errors))

    def test_prod_cannot_enable_optional_analytics(self) -> None:
        params = self.root / "infra/environments/prod.bicepparam"
        params.write_text(params.read_text(encoding="utf-8").replace(
            "param enableAdvancedAnalytics = false", "param enableAdvancedAnalytics = true"), encoding="utf-8")

        errors = validate_profiles(self.root)
        self.assertTrue(any("prod" in error and "advanced analytics" in error for error in errors))

    def test_non_dev_foundation_module_must_remain_guarded(self) -> None:
        main = self.root / "infra/main.bicep"
        main.write_text(main.read_text(encoding="utf-8").replace(
            "if (enableDevFoundation && environment == 'dev')", "if (enableDevFoundation)"), encoding="utf-8")

        errors = validate_profiles(self.root)
        self.assertTrue(any("guard network" in error for error in errors))
        self.assertTrue(any("guard vault" in error for error in errors))

    def test_comment_cannot_substitute_for_active_environment_restriction(self) -> None:
        main = self.root / "infra/main.bicep"
        main.write_text(main.read_text(encoding="utf-8").replace(
            "@allowed(['dev', 'stage', 'prod'])", "// @allowed(['dev', 'stage', 'prod'])"), encoding="utf-8")

        errors = validate_profiles(self.root)
        self.assertTrue(any("restrict environment" in error for error in errors))

    def test_multiline_string_cannot_substitute_for_active_environment_or_module_guards(self) -> None:
        main = self.root / "infra/main.bicep"
        source = main.read_text(encoding="utf-8")
        source = source.replace("@allowed(['dev', 'stage', 'prod'])", "@allowed(['dev'])")
        source = source.replace(
            "if (enableDevFoundation && environment == 'dev')",
            "if (enableDevFoundation)",
        )
        source += """

var deceptiveSource = '''
@allowed(['dev', 'stage', 'prod'])
param environment string
module network './modules/networking.bicep' = if (enableDevFoundation && environment == 'dev') {
module vault './modules/key-vault.bicep' = if (enableDevFoundation && environment == 'dev') {
'''
"""
        main.write_text(source, encoding="utf-8")

        errors = validate_profiles(self.root)

        self.assertTrue(any("restrict environment" in error for error in errors))
        self.assertTrue(any("guard network" in error for error in errors))
        self.assertTrue(any("guard vault" in error for error in errors))

    def test_json_only_stage_network_override_is_rejected(self) -> None:
        import json

        profile = self.root / "infra/environments/stage.json"
        value = json.loads(profile.read_text(encoding="utf-8"))
        value["parameters"]["virtualNetworkCidr"] = {"value": "10.42.0.0/16"}
        profile.write_text(json.dumps(value), encoding="utf-8")

        errors = validate_profiles(self.root)
        self.assertTrue(any("stage" in error and "presence of optional parameter virtualNetworkCidr" in error for error in errors))
        self.assertTrue(any("stage" in error and "DEV-only parameter virtualNetworkCidr" in error for error in errors))

    def _set_dev_cidr(self, value: str) -> None:
        import json

        profile = self.root / "infra/environments/dev.json"
        parsed = json.loads(profile.read_text(encoding="utf-8"))
        parsed["parameters"]["virtualNetworkCidr"]["value"] = value
        profile.write_text(json.dumps(parsed), encoding="utf-8")
        params = self.root / "infra/environments/dev.bicepparam"
        source = params.read_text(encoding="utf-8")
        source = source.replace("param virtualNetworkCidr = '10.42.0.0/16'", f"param virtualNetworkCidr = '{value}'")
        params.write_text(source, encoding="utf-8")

    def test_valid_minimum_vnet_prefix_supports_both_planned_subnets(self) -> None:
        self._set_dev_cidr("10.42.0.0/22")

        errors = validate_profiles(self.root)

        self.assertEqual([], errors)

    def test_vnet_too_small_for_subnet_index_two_is_rejected(self) -> None:
        self._set_dev_cidr("10.42.0.0/23")

        errors = validate_profiles(self.root)

        self.assertTrue(any("must be /22 or larger" in error for error in errors))

    def test_noncanonical_cidr_with_host_bits_is_rejected(self) -> None:
        self._set_dev_cidr("10.42.0.1/16")

        errors = validate_profiles(self.root)

        self.assertTrue(any("canonical IPv4 CIDR" in error for error in errors))

    def test_ipv6_cidr_is_rejected(self) -> None:
        self._set_dev_cidr("2001:db8::/32")

        errors = validate_profiles(self.root)

        self.assertTrue(any("must be an IPv4 CIDR network" in error for error in errors))

    def test_malformed_cidr_is_rejected(self) -> None:
        self._set_dev_cidr("not-a-network")

        errors = validate_profiles(self.root)

        self.assertTrue(any("canonical IPv4 CIDR" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
