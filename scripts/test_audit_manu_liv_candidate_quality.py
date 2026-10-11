"""Tests for the source-only football data quality audit."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
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
        "match": {"sourceClockUnit": "seconds", "sequenceStart": 1},
        "coordinates": {"unit": None, "axisConvention": None, "pitchDimensionsMetres": None,
                        "status": "unverified"},
        "playerMetrics": {"status": "unvalidated", "unitsAndValidity": "unverified",
                           "mayBePresentedAsObservedPerformance": False},
        "canonicalization": {"mappingStatus": "not_approved"},
    }
    return audit.audit_data(data, manifest, domain_source)


class CandidateQualityAuditTests(unittest.TestCase):
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
