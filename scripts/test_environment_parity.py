import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
INFRA_DIR = REPO_ROOT / "infra"
ENV_DIR = INFRA_DIR / "environments"

class TestEnvironmentParity(unittest.TestCase):
    def setUp(self):
        self.assertTrue(ENV_DIR.exists(), f"Environment directory {ENV_DIR} must exist")

    def test_environment_bicepparams_exist(self):
        for env in ["dev", "stage", "prod"]:
            bicepparam = ENV_DIR / f"{env}.bicepparam"
            self.assertTrue(bicepparam.exists(), f"Missing {bicepparam}")

    def test_prod_and_stage_strictly_disable_dev_foundation(self):
        for env in ["stage", "prod"]:
            bicepparam = ENV_DIR / f"{env}.bicepparam"
            content = bicepparam.read_text(encoding="utf-8")
            self.assertIn("param enableDevFoundation = false", content,
                          f"{env}.bicepparam must strictly set enableDevFoundation = false")
            self.assertIn("param enableAdvancedAnalytics = false", content,
                          f"{env}.bicepparam must strictly set enableAdvancedAnalytics = false")

    def test_dev_foundation_enabled_only_for_dev(self):
        dev_bicepparam = ENV_DIR / "dev.bicepparam"
        content = dev_bicepparam.read_text(encoding="utf-8")
        self.assertIn("param environment = 'dev'", content)
        self.assertIn("param enableDevFoundation = true", content)

    def test_no_hardcoded_secrets_in_environment_files(self):
        secret_patterns = [
            re.compile(r"password\s*=", re.IGNORECASE),
            re.compile(r"secret\s*=", re.IGNORECASE),
            re.compile(r"connectionstring\s*=", re.IGNORECASE),
            re.compile(r"apikey\s*=", re.IGNORECASE),
        ]
        for param_file in ENV_DIR.glob("*.bicepparam"):
            content = param_file.read_text(encoding="utf-8")
            for pattern in secret_patterns:
                self.assertIsNone(pattern.search(content),
                                  f"Potential secret pattern found in {param_file.name}")

if __name__ == "__main__":
    unittest.main()
