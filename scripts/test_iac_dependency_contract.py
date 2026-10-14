"""Regression tests for EP02-S01 dependency and reachability claims."""
from __future__ import annotations

import copy
import tempfile
from pathlib import Path
import unittest

from scripts import validate_iac_dependency_contract as validator


class DependencyContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.contract = validator._load(validator.CONTRACT)

    def test_repository_contract_matches_declared_bicep_symbols(self) -> None:
        self.assertEqual(27, validator.validate(self.contract))

    def test_private_resource_cannot_be_marked_available_without_connectivity(self) -> None:
        contract = copy.deepcopy(self.contract)
        storage = next(item for item in contract["resources"] if item["id"] == "storage-account")
        storage["disposition"] = "available"
        with self.assertRaisesRegex(validator.ContractError, "Available disposition"):
            validator.validate(contract)

    def test_verified_private_path_requires_a_reviewable_evidence_file(self) -> None:
        contract = copy.deepcopy(self.contract)
        storage = next(item for item in contract["resources"] if item["id"] == "storage-account")
        storage["connectivity"] = {"status": "verified", "evidence": {"path": "missing.md", "testId": "private-dns-smoke"}}
        with self.assertRaisesRegex(validator.ContractError, "is missing or outside the repository"):
            validator.validate(contract)

    def test_connectivity_evidence_cannot_escape_repository(self) -> None:
        contract = copy.deepcopy(self.contract)
        storage = next(item for item in contract["resources"] if item["id"] == "storage-account")
        storage["connectivity"] = {"status": "verified", "evidence": {"path": "../../etc/hosts", "testId": "private-dns-smoke"}}
        with self.assertRaisesRegex(validator.ContractError, "Unsafe Connectivity evidence"):
            validator.validate(contract)

    def test_symlinked_directory_path_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            target = root / "real"
            target.mkdir()
            (target / "evidence.md").write_text("evidence", encoding="utf-8")
            (root / "linked").symlink_to(target, target_is_directory=True)
            with self.assertRaisesRegex(validator.ContractError, "symlinked"):
                validator._repository_file(root, "linked/evidence.md", "Test evidence")

    def test_candidate_resource_requires_declared_opt_in_gate(self) -> None:
        contract = copy.deepcopy(self.contract)
        postgres = next(item for item in contract["resources"] if item["id"] == "canonical-postgres-candidate")
        postgres["source"]["gate"] = "otherFlag"
        with self.assertRaisesRegex(validator.ContractError, "opt-in gate"):
            validator.validate(contract)

    def test_missing_required_canonical_workload_is_rejected(self) -> None:
        contract = copy.deepcopy(self.contract)
        contract["resources"] = [item for item in contract["resources"] if item["id"] != "canonical-event-worker"]
        with self.assertRaisesRegex(validator.ContractError, "Required dependency rows"):
            validator.validate(contract)

    def test_duplicate_resource_id_is_rejected(self) -> None:
        contract = copy.deepcopy(self.contract)
        contract["resources"].append(copy.deepcopy(contract["resources"][0]))
        with self.assertRaisesRegex(validator.ContractError, "unique"):
            validator.validate(contract)


if __name__ == "__main__":
    unittest.main()
