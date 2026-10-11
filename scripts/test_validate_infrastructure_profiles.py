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

    def test_json_only_stage_network_override_is_rejected(self) -> None:
        import json

        profile = self.root / "infra/environments/stage.json"
        value = json.loads(profile.read_text(encoding="utf-8"))
        value["parameters"]["virtualNetworkCidr"] = {"value": "10.42.0.0/16"}
        profile.write_text(json.dumps(value), encoding="utf-8")

        errors = validate_profiles(self.root)
        self.assertTrue(any("stage" in error and "presence of optional parameter virtualNetworkCidr" in error for error in errors))
        self.assertTrue(any("stage" in error and "DEV-only parameter virtualNetworkCidr" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
