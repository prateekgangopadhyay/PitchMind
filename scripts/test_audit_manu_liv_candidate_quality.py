"""Tests for the source-only football data quality audit."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("audit_manu_liv_candidate_quality.py")
spec = importlib.util.spec_from_file_location("candidate_quality_audit", SCRIPT)
audit = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(audit)

HOME = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
AWAY = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
MATCH = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"


def position(team_id=HOME, team_name="Synthetic Home", player_name="Player One", jersey=1, **overrides):
    values = {
        "teamId": team_id, "teamName": team_name, "playerName": player_name,
        "jerseyNumber": jersey, "position": "MID", "x": 50.0, "y": 45.0,
        "speed": 2.0, "distanceCovered": 0.0, "heatIndex": 0.5,
        "passAccuracy": 80.0, "touches": 0, "stamina": 90.0, "heartRate": 120,
    }
    return {**values, **overrides}


def event(sequence, event_type="Pass", *, clock=None, team_id=HOME, team_name="Synthetic Home",
          positions=None, metadata=None, player_name="Player One", outcome="Complete"):
    return {
        "eventId": f"00000000-0000-4000-8000-{sequence:012d}",
        "matchId": MATCH,
        "sequence": sequence,
        "clockSeconds": sequence if clock is None else clock,
        "period": 1,
        "eventType": event_type,
        "teamId": team_id,
        "teamName": team_name,
        "outcome": outcome,
        "metadata": metadata or {},
        "playerPositions": positions if positions is not None else [position(team_id, team_name)],
        "pitchX": 40.0,
        "pitchY": 50.0,
        "ballX": 41.0,
        "ballY": 51.0,
        "endX": None,
        "endY": None,
        "playerName": player_name,
        "receiverName": None,
    }


def report_for(events, domain_source="public enum MatchEventType { Pass, Goal, Pressure }"):
    data = json.dumps(events, separators=(",", ":")).encode()
    manifest = {
        "manifestVersion": "test",
        "status": "candidate_not_canonical",
        "source": {"path": "fixture.json", "sha256": audit.sha256(data).hexdigest(),
                   "byteSize": len(data), "expectedEventCount": len(events)},
        "match": {"sourceClockUnit": "unverified", "sequenceStart": 1},
        "coordinates": {"unit": None, "axisConvention": None, "pitchDimensionsMetres": None,
                        "status": "unverified"},
        "playerMetrics": {"status": "unvalidated", "unitsAndValidity": "unverified",
                           "mayBePresentedAsObservedPerformance": False},
        "canonicalization": {"mappingStatus": "not_approved"},
    }
    return audit.audit_data(data, manifest, domain_source)


class CandidateQualityAuditTests(unittest.TestCase):
    def test_clock_value_difference_does_not_claim_elapsed_seconds(self):
        report = report_for([event(1, clock=10), event(2, clock=40)])
        chronology = report["chronology"]

        self.assertEqual(chronology["clockUnitDeclaredByManifest"], "unverified")
        self.assertEqual(chronology["rawClockValueRange"], {
            "minimum": 10, "maximum": 40, "difference": 30, "unit": "unverified",
        })
        self.assertNotIn("elapsedRangeSecondsFromSourceValues", chronology)

    def test_metadata_period_comparison_preserves_exact_value_and_json_type_without_authority(self):
        events = [
            event(1, metadata={"period": 1}),
            event(2, metadata={"period": "1"}),
            event(3),
        ]

        comparison = report_for(events)["chronology"]["metadataPeriodFieldComparison"]

        self.assertEqual(comparison["recordsWithMetadataPeriod"], 2)
        self.assertEqual(comparison["exactValueAndJsonTypeMatches"], 1)
        self.assertEqual(comparison["mismatchCount"], 1)
        self.assertEqual(comparison["mismatchExamples"], [{
            "sequence": 2,
            "eventType": "Pass",
            "topLevelPeriod": {"jsonType": "integer", "value": 1},
            "metadataPeriod": {"jsonType": "string", "value": "1", "valueTruncated": False},
        }])
        self.assertIn("neither field is treated as authoritative", comparison["interpretation"])

    def test_pinned_candidate_reports_known_metadata_period_conflict(self):
        root = Path(__file__).resolve().parents[1]
        source = (root / "data/simulations/manu-liv-match-dataset.json").read_bytes()
        manifest = json.loads((root / "data/simulations/manu-liv-candidate-manifest.json").read_text(encoding="utf-8"))
        domain = (root / audit.CANONICAL_ENUM).read_text(encoding="utf-8")

        comparison = audit.audit_data(source, manifest, domain)["chronology"]["metadataPeriodFieldComparison"]

        self.assertEqual(comparison["recordsWithMetadataPeriod"], 3)
        self.assertEqual(comparison["exactValueAndJsonTypeMatches"], 0)
        self.assertEqual(comparison["mismatchCount"], 3)
        self.assertEqual([row["sequence"] for row in comparison["mismatchExamples"]], [1, 1037, 1038])
        self.assertEqual(comparison["mismatchExamples"][0], {
            "sequence": 1,
            "eventType": "KickOff",
            "topLevelPeriod": {"jsonType": "integer", "value": 1},
            "metadataPeriod": {"jsonType": "string", "value": "1", "valueTruncated": False},
        })
        self.assertEqual(comparison["mismatchExamples"][1], {
            "sequence": 1037,
            "eventType": "HalfTime",
            "topLevelPeriod": {"jsonType": "integer", "value": 2},
            "metadataPeriod": {"jsonType": "string", "value": "1", "valueTruncated": False},
        })
        self.assertEqual(comparison["mismatchExamples"][2], {
            "sequence": 1038,
            "eventType": "KickOff",
            "topLevelPeriod": {"jsonType": "integer", "value": 2},
            "metadataPeriod": {"jsonType": "string", "value": "2", "valueTruncated": False},
        })
        self.assertFalse(comparison["interpretation"].startswith("Top-level period is authoritative"))

    def test_metadata_period_mismatch_examples_are_bounded(self):
        long_period = "x" * 200
        events = [event(index + 1, metadata={"period": long_period}) for index in range(25)]

        comparison = report_for(events)["chronology"]["metadataPeriodFieldComparison"]

        self.assertEqual(comparison["mismatchCount"], 25)
        self.assertEqual(len(comparison["mismatchExamples"]), 20)
        self.assertEqual(len(comparison["mismatchExamples"][0]["metadataPeriod"]["value"]), 128)
        self.assertTrue(comparison["mismatchExamples"][0]["metadataPeriod"]["valueTruncated"])

    def test_report_output_cannot_alias_and_overwrite_source_or_manifest(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source = root / "candidate.json"
            manifest = root / "manifest.json"
            domain_source = root / "MatchEvent.cs"
            source_bytes = json.dumps([event(1)], separators=(",", ":")).encode()
            source.write_bytes(source_bytes)
            manifest_bytes = json.dumps({
                "source": {
                    "path": source.name,
                    "sha256": audit.sha256(source_bytes).hexdigest(),
                    "byteSize": len(source_bytes),
                    "expectedEventCount": 1,
                },
                "coordinates": {"status": "unverified"},
                "playerMetrics": {"status": "unvalidated"},
                "canonicalization": {"mappingStatus": "not_approved"},
            }).encode()
            manifest.write_bytes(manifest_bytes)
            domain_source.write_text("public enum MatchEventType { Pass }", encoding="utf-8")

            domain_bytes = domain_source.read_bytes()
            for protected_path, protected_bytes in (
                (source, source_bytes),
                (manifest, manifest_bytes),
                (domain_source, domain_bytes),
            ):
                with self.subTest(protected=protected_path.name, alias="direct"):
                    self.assertEqual(self._run_audit(source, manifest, domain_source, protected_path), 2)
                    self.assertEqual(protected_path.read_bytes(), protected_bytes)

                for alias_kind in ("symlink", "hardlink"):
                    alias = root / f"{protected_path.name}-{alias_kind}-alias"
                    try:
                        if alias_kind == "symlink":
                            alias.symlink_to(protected_path)
                        else:
                            alias.hardlink_to(protected_path)
                    except (OSError, NotImplementedError) as exc:
                        self.skipTest(f"{alias_kind} aliases are unavailable: {exc}")
                    with self.subTest(protected=protected_path.name, alias=alias_kind):
                        self.assertEqual(self._run_audit(source, manifest, domain_source, alias), 2)
                        self.assertEqual(protected_path.read_bytes(), protected_bytes)

    @staticmethod
    def _run_audit(source: Path, manifest: Path, domain_source: Path, output: Path) -> int:
        import contextlib
        import io

        with contextlib.redirect_stderr(io.StringIO()):
            return audit.main([
                "--source", str(source), "--manifest", str(manifest),
                "--domain-source", str(domain_source), "--output", str(output),
            ])

    def test_coordinate_range_is_reported_without_assigning_units(self):
        row = event(1, positions=[position(x=0, y=100)])
        row["pitchX"] = 100.0
        report = report_for([row])
        coordinates = report["coordinateAudit"]

        self.assertIsNone(coordinates["manifestUnit"])
        self.assertEqual(coordinates["manifestStatus"], "unverified")
        self.assertEqual(coordinates["rawFieldRanges"]["pitchX"]["maximum"], 100.0)
        self.assertEqual(coordinates["rawFieldRanges"]["playerX"]["minimum"], 0)
        self.assertEqual(coordinates["rawFieldRanges"]["playerX"]["outsideZeroToHundredCount"], 0)
        self.assertIn("units", coordinates["interpretation"])
        self.assertIn("not a verified validity bound", coordinates["interpretation"])

    def test_metric_variation_and_source_order_are_descriptive_only(self):
        first = event(1, positions=[position(distanceCovered=0.0, touches=0)])
        second = event(2, positions=[position(distanceCovered=1.2, touches=5)])
        report = report_for([first, second])
        metrics = report["playerMetricAudit"]["fields"]

        self.assertEqual(metrics["distanceCovered"]["minimum"], 0.0)
        self.assertEqual(metrics["distanceCovered"]["maximum"], 1.2)
        self.assertEqual(metrics["distanceCovered"]["adjacentDecreasesInSourceOrder"], 0)
        self.assertEqual(metrics["distanceCovered"]["identitiesWithChangingValues"], 1)
        self.assertEqual(metrics["distanceCovered"]["identitiesWithConstantValues"], 0)
        self.assertEqual(metrics["touches"]["distinctNumericValueCount"], 2)
        self.assertEqual(report["playerMetricAudit"]["manifestUnitsAndValidity"], "unverified")
        self.assertIn("meaning", report["playerMetricAudit"]["interpretation"])

    def test_score_snapshots_are_reported_without_mapping_score_sides_to_teams(self):
        events = [
            event(1, "Goal", metadata={"score": "1-0", "teamName": "Synthetic Home",
                                        "playerName": "Player One", "outcome": "Goal"}, outcome="Goal"),
            event(2, "Goal", team_id=AWAY, team_name="Synthetic Away",
                  positions=[position(AWAY, "Synthetic Away")],
                  metadata={"score": "1-1", "teamName": "Synthetic Away",
                            "playerName": "Player One", "outcome": "Goal"}, outcome="Goal"),
            event(3, "FullTime", metadata={"score": "1-1", "result": "Draw"}),
        ]
        report = report_for(events)
        football = report["footballEventAudit"]

        self.assertEqual([goal["metadataScore"] for goal in football["goalRows"]], ["1-0", "1-1"])
        self.assertEqual(football["adjacentGoalScoreTransitionsWithExactlyOneNondecreasingComponentIncrement"], 1)
        self.assertEqual(football["fullTimeMetadataScore"], "1-1")
        self.assertIn("score-side order", football["interpretation"])

    def test_unparseable_goal_score_does_not_bridge_adjacent_score_comparison(self):
        scores = ["1-0", "unknown", "1-1"]
        events = [
            event(index + 1, "Goal", metadata={"score": score})
            for index, score in enumerate(scores)
        ]
        football = report_for(events)["footballEventAudit"]

        self.assertEqual(football["goalScoreSnapshotsParseable"], 2)
        self.assertEqual(football["goalScoreSnapshotsUnparseable"], 1)
        self.assertEqual(
            football["adjacentGoalScoreTransitionsWithExactlyOneNondecreasingComponentIncrement"],
            0,
        )

    def test_red_card_audit_reports_source_frame_presence_without_adjudicating_roster_truth(self):
        events = [
            event(1, positions=[position(player_name="SentOff")]),
            event(2, "RedCard", player_name="SentOff", outcome="Card",
                  metadata={"playerName": "SentOff"}, positions=[position(player_name="SentOff")]),
            event(3, positions=[position(player_name="RemainingPlayer", jersey=2)]),
        ]
        football = report_for(events)["footballEventAudit"]
        snapshots = football["redCardFrameSnapshots"][0]["snapshots"]

        self.assertTrue(snapshots["before"]["eventParticipantLabelPresent"])
        self.assertTrue(snapshots["atEvent"]["eventParticipantLabelPresent"])
        self.assertFalse(snapshots["after"]["eventParticipantLabelPresent"])

    def test_event_type_comparison_reports_exact_names_only(self):
        events = [event(1, "PressureApplied"), event(2, "Pass")]
        report = report_for(events, "public enum MatchEventType { Pass, Goal, Pressure }")
        comparison = report["eventTypeComparison"]

        self.assertEqual(comparison["exactNameOverlapOnly"], ["Pass"])
        self.assertEqual(comparison["sourceTypesWithoutExactNameMatch"], ["PressureApplied"])
        self.assertEqual(comparison["domainTypesWithoutExactSourceName"], ["Goal", "Pressure"])
        self.assertIn("No semantic", comparison["interpretation"])

    def test_source_hash_mismatch_is_rejected(self):
        data = b"[]"
        manifest = {"source": {"sha256": "0" * 64, "byteSize": len(data)}}
        with self.assertRaisesRegex(audit.AuditError, "do not match"):
            audit.audit_data(data, manifest, "public enum MatchEventType { Pass }")


if __name__ == "__main__":
    unittest.main()
