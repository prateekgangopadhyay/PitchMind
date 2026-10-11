#!/usr/bin/env python3
"""Checks that the pinned Syft wrapper emits a valid SPDX JSON document."""

import json
import pathlib
import subprocess
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
GENERATOR = ROOT / "scripts" / "generate-sbom.sh"


class GenerateSbomTests(unittest.TestCase):
    def test_generates_spdx_json_outside_read_only_source(self):
        with tempfile.TemporaryDirectory(prefix="pitchmind-sbom-") as temp_dir:
            root = pathlib.Path(temp_dir)
            source = root / "source"
            output_dir = root / "output"
            source.mkdir()
            (source / "package.json").write_text(
                '{"name":"pitchmind-sbom-fixture","version":"1.0.0"}\n',
                encoding="utf-8",
            )
            output = output_dir / "fixture.spdx.json"

            result = subprocess.run(
                ["bash", str(GENERATOR), str(source), str(output)],
                capture_output=True,
                text=True,
                timeout=120,
                check=False,
            )

            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            document = json.loads(output.read_text(encoding="utf-8"))

        self.assertEqual(document["spdxVersion"], "SPDX-2.3")
        self.assertTrue(document["SPDXID"].startswith("SPDXRef-"))
        self.assertIn("packages", document)

    def test_output_inside_source_is_rejected_without_creating_directories(self):
        with tempfile.TemporaryDirectory(prefix="pitchmind-sbom-path-") as temp_dir:
            source = pathlib.Path(temp_dir, "source")
            source.mkdir()
            output = source / "generated" / "pitchmind.spdx.json"

            result = subprocess.run(
                ["bash", str(GENERATOR), str(source), str(output)],
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )

            self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
            self.assertFalse(output.parent.exists())

    def test_existing_symlink_output_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix="pitchmind-sbom-symlink-") as temp_dir:
            root = pathlib.Path(temp_dir)
            source = root / "source"
            source.mkdir()
            target = root / "target.spdx.json"
            target.write_text("preserve", encoding="utf-8")
            output = root / "alias.spdx.json"
            output.symlink_to(target)

            result = subprocess.run(
                ["bash", str(GENERATOR), str(source), str(output)],
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )

            self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
            self.assertEqual(target.read_text(encoding="utf-8"), "preserve")


if __name__ == "__main__":
    unittest.main()
