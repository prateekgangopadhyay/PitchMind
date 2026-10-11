"""Integrity and non-canonicalization tests for the pinned candidate feed."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("validate_manu_liv_candidate.py")
spec = importlib.util.spec_from_file_location("manu_liv_candidate", SCRIPT)
validator = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(validator)
ROOT = SCRIPT.parent.parent


def position(team_id: str, team_name: str, name: str = "Player One", jersey: int = 1) -> dict:
    return {
        "playerName": name, "teamName": team_name, "teamId": team_id, "jerseyNumber": jersey,
        "position": "MID", "x": 50.0, "y": 45.0, "speed": 2.3, "distanceCovered": 1.1,
        "heatIndex": 0.2, "passAccuracy": 90.0, "touches": 3, "stamina": 80.0, "heartRate": 100,
    }


def event(event_id: str, sequence: int, clock: int, event_type: str = "Pass") -> dict:
    team_id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    return {
        "eventId": event_id, "matchId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        "sequence": sequence, "clockSeconds": clock, "period": 1, "eventType": event_type,
        "teamId": team_id, "teamName": "Synthetic Home", "outcome": "complete", "metadata": {},
        "playerPositions": [position(team_id, "Synthetic Home")], "pitchX": 50.0, "pitchY": 45.0,
        "ballX": 50.0, "ballY": 45.0, "endX": None, "endY": None,
        "playerName": "Player One", "receiverName": None,
    }


def test_manifest(events: list[dict], payload: bytes) -> dict:
    return {
        "manifestVersion": "test", "fixtureId": "test-candidate", "status": "candidate_not_canonical",
        "source": {
            "sha256": validator.sha256(payload).hexdigest(), "byteSize": len(payload),
            "expectedEventCount": len(events), "sequenceStart": 1, "sequenceEnd": len(events), "synthetic": True,
            "provenance": "repository-owner-confirmed", "redistributionPermission": "repository-owner-confirmed",
            "independentRightsPrivacyReview": "pending",
        },
        "match": {
            "expectedMatchCount": 1, "expectedTeamCount": 1, "sourceClockUnit": "seconds",
            "expectedPeriods": [1], "sourceClockStart": events[0]["clockSeconds"],
            "sourceClockEnd": events[-1]["clockSeconds"],
        },
        "coordinates": {"status": "unverified", "unit": None, "axisConvention": None, "pitchDimensionsMetres": None},
        "playerMetrics": {"status": "unvalidated", "unitsAndValidity": "unverified", "mayBePresentedAsObservedPerformance": False},
        "canonicalization": {"mappingStatus": "not_approved", "mayCreateCanonicalEvents": False,
                             "sourceEventTypes": sorted({e["eventType"] for e in events})},
    }


class CandidateValidationTests(unittest.TestCase):
    def setUp(self):
        self.events = [
            event("11111111-1111-4111-8111-111111111111", 1, 0, "KickOff"),
            event("22222222-2222-4222-8222-222222222222", 2, 12, "FullTime"),
        ]
        self.payload = json.dumps(self.events, separators=(",", ":")).encode()
        self.manifest = test_manifest(self.events, self.payload)

    def test_preserves_order_and_reproduces_source_digests_without_mapping(self):
        first = validator.validate_source(self.payload, self.manifest)
        second = validator.validate_source(self.payload, self.manifest)
        self.assertEqual(first["sourceSequence"], {"first": 1, "last": 2, "preserved": True})
        self.assertEqual(first["sourceClock"]["last"], 12)
        self.assertTrue(first["sourceClock"]["preservedWithoutNormalization"])
        self.assertEqual(first["sourceEventIdLedgerSha256"], second["sourceEventIdLedgerSha256"])
        self.assertEqual(first["orderedEventRecordsSha256"], second["orderedEventRecordsSha256"])
        self.assertEqual(first["status"], "structurally_valid_candidate_blocked_for_canonicalization")
        self.assertFalse(first["canonicalMappingsApproved"])
        self.assertFalse(first["coordinateUnitsVerified"])
        self.assertFalse(first["playerMetricsValidated"])

    def test_ordered_record_digest_covers_fields_beyond_identity_and_time(self):
        baseline = validator.validate_source(self.payload, self.manifest)
        changed_events = [{**self.events[0], "outcome": "incomplete"}, self.events[1]]
        changed_payload = json.dumps(changed_events, separators=(",", ":")).encode()
        changed_manifest = test_manifest(changed_events, changed_payload)
        changed = validator.validate_source(changed_payload, changed_manifest)

        self.assertEqual(baseline["sourceEventIdLedgerSha256"], changed["sourceEventIdLedgerSha256"])
        self.assertNotEqual(baseline["orderedEventRecordsSha256"], changed["orderedEventRecordsSha256"])

    def test_rejects_duplicate_source_event_ids(self):
        invalid = [self.events[0], {**self.events[1], "eventId": self.events[0]["eventId"]}]
        payload = json.dumps(invalid).encode()
        with self.assertRaisesRegex(validator.CandidateValidationError, "Duplicate source event ID"):
            validator.validate_source(payload, test_manifest(invalid, payload))

    def test_rejects_duplicate_event_ids_with_different_uuid_case(self):
        invalid = [
            {**self.events[0], "eventId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"},
            {**self.events[1], "eventId": "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA"},
        ]
        payload = json.dumps(invalid).encode()
        with self.assertRaisesRegex(validator.CandidateValidationError, "Duplicate source event ID"):
            validator.validate_source(payload, test_manifest(invalid, payload))

    def test_distinct_match_count_uses_uuid_identity_not_source_spelling(self):
        events = [
            self.events[0],
            {**self.events[1], "matchId": self.events[1]["matchId"].upper()},
        ]
        payload = json.dumps(events).encode()
        manifest = test_manifest(events, payload)
        manifest["match"]["expectedMatchCount"] = 2

        with self.assertRaisesRegex(validator.CandidateValidationError, "Distinct match count differs"):
            validator.validate_source(payload, manifest)

    def test_uuid_identity_comparison_preserves_original_id_spelling_in_digest(self):
        events = [
            {**self.events[0], "eventId": "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA"},
            self.events[1],
        ]
        payload = json.dumps(events).encode()
        report = validator.validate_source(payload, test_manifest(events, payload))
        original_id_ledger = "".join(f"{event['eventId']}\n" for event in events).encode()

        self.assertEqual(
            report["sourceEventIdLedgerSha256"],
            validator.sha256(original_id_ledger).hexdigest(),
        )

    def test_same_player_label_and_jersey_are_distinct_across_source_teams(self):
        home_id = self.events[0]["teamId"]
        away_id = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
        away = event("33333333-3333-4333-8333-333333333333", 2, 12, "FullTime")
        away["teamId"] = away_id
        away["teamName"] = "Synthetic Away"
        away["playerPositions"] = [position(away_id, "Synthetic Away", "Alex Smith", 7)]
        frame_with_both_teams = {
            **self.events[0],
            "playerPositions": [
                position(home_id, "Synthetic Home", "Alex Smith", 7),
                position(away_id, "Synthetic Away", "Alex Smith", 7),
            ],
        }
        events = [frame_with_both_teams, away]
        payload = json.dumps(events, separators=(",", ":")).encode()
        manifest = test_manifest(events, payload)
        manifest["match"]["expectedTeamCount"] = 2

        report = validator.validate_source(payload, manifest)

        self.assertEqual(report["teamCount"], 2)
        self.assertEqual(report["attachedPositionCount"], 3)

    def test_duplicate_player_label_and_jersey_within_same_team_is_rejected(self):
        team_id = self.events[0]["teamId"]
        duplicate = position(team_id, "Synthetic Home", "Alex Smith", 7)
        events = [
            {
                **self.events[0],
                "playerPositions": [duplicate, dict(duplicate)],
            },
            self.events[1],
        ]
        payload = json.dumps(events, separators=(",", ":")).encode()

        with self.assertRaisesRegex(validator.CandidateValidationError, "Duplicate player row in frame"):
            validator.validate_source(payload, test_manifest(events, payload))

    def test_event_team_must_appear_in_its_attached_position_frame(self):
        home_id = self.events[0]["teamId"]
        away_id = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
        events = [
            {
                **self.events[0],
                "playerPositions": [position(away_id, "Synthetic Away")],
            },
            {
                **self.events[1],
                "eventType": "Pass",
                "teamId": away_id,
                "teamName": "Synthetic Away",
                "playerPositions": [
                    position(home_id, "Synthetic Home"),
                    position(away_id, "Synthetic Away", "Alex Smith", 7),
                ],
            },
        ]
        payload = json.dumps(events, separators=(",", ":")).encode()
        manifest = test_manifest(events, payload)
        manifest["match"]["expectedTeamCount"] = 2

        with self.assertRaisesRegex(
            validator.CandidateValidationError,
            "Event team identity is absent from its attached position frame",
        ):
            validator.validate_source(payload, manifest)

    def test_rejects_sequence_gaps_and_clock_regression(self):
        invalid_sequence = [self.events[0], {**self.events[1], "sequence": 3}]
        payload = json.dumps(invalid_sequence).encode()
        with self.assertRaisesRegex(validator.CandidateValidationError, "sequence is not contiguous"):
            validator.validate_source(payload, test_manifest(invalid_sequence, payload))
        invalid_clock = [self.events[0], {**self.events[1], "clockSeconds": -1}]
        payload = json.dumps(invalid_clock).encode()
        invalid_clock_manifest = test_manifest(invalid_clock, payload)
        invalid_clock_manifest["match"]["sourceClockEnd"] = self.events[-1]["clockSeconds"]
        with self.assertRaisesRegex(validator.CandidateValidationError, "clock is not an ordered integer"):
            validator.validate_source(payload, invalid_clock_manifest)

    def test_rejects_float_sequence_values(self):
        invalid = [self.events[0], {**self.events[1], "sequence": 2.0}]
        payload = json.dumps(invalid).encode()
        with self.assertRaisesRegex(validator.CandidateValidationError, "sequence is not contiguous"):
            validator.validate_source(payload, test_manifest(invalid, payload))

    def test_rejects_duplicate_json_object_keys(self):
        with self.assertRaisesRegex(validator.CandidateValidationError, "Duplicate JSON object key"):
            validator._strict_json_loads('{"sequence":1,"sequence":2}')

    def test_rejects_overflowing_json_number_inside_uninterpreted_metadata(self):
        payload = json.dumps(self.events).encode().replace(
            b'"metadata": {}', b'"metadata": {"sourceValue": 1e999}', 1
        )
        self.assertIn(b"1e999", payload)
        with self.assertRaisesRegex(validator.CandidateValidationError, "Non-finite JSON number"):
            validator.validate_source(payload, test_manifest(self.events, payload))

    def test_rejects_oversized_json_integer_inside_uninterpreted_metadata(self):
        payload = json.dumps(self.events).encode().replace(
            b'"metadata": {}', b'"metadata": {"sourceValue": ' + (b"9" * 5000) + b"}", 1
        )
        with self.assertRaisesRegex(validator.CandidateValidationError, "JSON integer.*supported size"):
            validator.validate_source(payload, test_manifest(self.events, payload))

    def test_rejects_malformed_manifest_types_without_leaking_type_errors(self):
        malformed = {**self.manifest, "match": {**self.manifest["match"], "expectedPeriods": "12"}}
        with self.assertRaisesRegex(validator.CandidateValidationError, "expectedPeriods"):
            validator.validate_source(self.payload, malformed)
        malformed_count = {**self.manifest, "source": {**self.manifest["source"], "expectedEventCount": "2"}}
        with self.assertRaisesRegex(validator.CandidateValidationError, "must be integers"):
            validator.validate_source(self.payload, malformed_count)

    def test_rejects_period_regression(self):
        invalid = [
            self.events[0],
            {**self.events[1], "period": 2},
            event("33333333-3333-4333-8333-333333333333", 3, 20),
        ]
        payload = json.dumps(invalid).encode()
        manifest = test_manifest(invalid, payload)
        manifest["match"]["expectedPeriods"] = [1, 2]
        manifest["match"]["requiredEventCounts"] = {"HalfTime": 1, "FullTime": 1}
        manifest["match"]["requiredEventMarkers"] = {
            "HalfTime": {"clockSeconds": 20, "period": 1},
            "FullTime": {"clockSeconds": 20, "period": 2},
        }
        with self.assertRaisesRegex(validator.CandidateValidationError, "period regressed"):
            validator.validate_source(payload, manifest)

    def test_rejects_half_time_marker_with_implausible_clock_and_period_crossing(self):
        invalid = [
            event("11111111-1111-4111-8111-111111111111", 1, 0, "KickOff"),
            event("22222222-2222-4222-8222-222222222222", 2, 1, "HalfTime"),
            event("33333333-3333-4333-8333-333333333333", 3, 2, "KickOff"),
            event("44444444-4444-4444-8444-444444444444", 4, 12, "FullTime"),
        ]
        invalid[1]["period"] = 2
        invalid[2]["period"] = 2
        invalid[3]["period"] = 2
        payload = json.dumps(invalid, separators=(",", ":")).encode()
        manifest = test_manifest(invalid, payload)
        manifest["match"]["expectedPeriods"] = [1, 2]
        manifest["match"]["requiredEventCounts"] = {"HalfTime": 1, "FullTime": 1}
        manifest["match"]["requiredEventMarkers"] = {
            "HalfTime": {"clockSeconds": 2700, "period": 2},
            "FullTime": {"clockSeconds": 12, "period": 2},
        }
        with self.assertRaisesRegex(validator.CandidateValidationError, "marker clock or period"):
            validator.validate_source(payload, manifest)

    def test_requires_explicit_event_policy_for_same_clock_period_two_event_before_half_time(self):
        events = [
            event("11111111-1111-4111-8111-111111111111", 1, 0, "KickOff"),
            event("22222222-2222-4222-8222-222222222222", 2, 10, "Pass"),
            event("33333333-3333-4333-8333-333333333333", 3, 10, "HalfTime"),
            event("44444444-4444-4444-8444-444444444444", 4, 10, "KickOff"),
            event("55555555-5555-4555-8555-555555555555", 5, 12, "FullTime"),
        ]
        events[1]["period"] = 2
        events[2]["period"] = 2
        events[3]["period"] = 2
        events[4]["period"] = 2
        payload = json.dumps(events, separators=(",", ":")).encode()
        manifest = test_manifest(events, payload)
        manifest["match"]["expectedPeriods"] = [1, 2]
        manifest["match"]["requiredEventCounts"] = {"HalfTime": 1, "FullTime": 1}
        manifest["match"]["requiredEventMarkers"] = {
            "HalfTime": {"clockSeconds": 10, "period": 2},
            "FullTime": {"clockSeconds": 12, "period": 2},
        }
        with self.assertRaisesRegex(validator.CandidateValidationError, "periods cross the half-time marker"):
            validator.validate_source(payload, manifest)
        manifest["match"]["allowedSameClockPeriod2EventTypesBeforeHalfTimeMarker"] = ["Pass"]
        self.assertEqual(validator.validate_source(payload, manifest)["status"],
                         "structurally_valid_candidate_blocked_for_canonicalization")
        events[1]["eventType"] = "Shot"
        payload = json.dumps(events, separators=(",", ":")).encode()
        manifest = test_manifest(events, payload)
        manifest["match"]["expectedPeriods"] = [1, 2]
        manifest["match"]["requiredEventCounts"] = {"HalfTime": 1, "FullTime": 1}
        manifest["match"]["requiredEventMarkers"] = {
            "HalfTime": {"clockSeconds": 10, "period": 2},
            "FullTime": {"clockSeconds": 12, "period": 2},
        }
        manifest["match"]["allowedSameClockPeriod2EventTypesBeforeHalfTimeMarker"] = ["Pass"]
        with self.assertRaisesRegex(validator.CandidateValidationError, "periods cross the half-time marker"):
            validator.validate_source(payload, manifest)

    def test_rejects_manifest_drift_and_unapproved_truth_changes(self):
        wrong_hash = {**self.manifest, "source": {**self.manifest["source"], "sha256": "0" * 64}}
        with self.assertRaisesRegex(validator.CandidateValidationError, "SHA-256"):
            validator.validate_source(self.payload, wrong_hash)
        approved_map = {**self.manifest, "canonicalization": {**self.manifest["canonicalization"], "mappingStatus": "approved"}}
        with self.assertRaisesRegex(validator.CandidateValidationError, "mappings must remain unapproved"):
            validator.validate_source(self.payload, approved_map)
        assumed_units = {**self.manifest, "coordinates": {"status": "unverified", "unit": "metres"}}
        with self.assertRaisesRegex(validator.CandidateValidationError, "Coordinate semantics must remain explicitly unverified"):
            validator.validate_source(self.payload, assumed_units)

    def test_requires_clock_and_period_for_each_required_period_end_marker(self):
        manifest = test_manifest(self.events, self.payload)
        manifest["match"]["expectedPeriods"] = [1, 2]
        manifest["match"]["requiredEventCounts"] = {"HalfTime": 1, "FullTime": 1}
        manifest["match"]["requiredEventMarkers"] = {
            "HalfTime": {"clockSeconds": 2700, "period": 1},
            "FullTime": {"clockSeconds": 5761, "period": 2},
        }
        for missing_marker in ("HalfTime", "FullTime"):
            incomplete = json.loads(json.dumps(manifest))
            del incomplete["match"]["requiredEventMarkers"][missing_marker]
            with self.assertRaisesRegex(validator.CandidateValidationError, "must pin clock and period"):
                validator.validate_source(self.payload, incomplete)

    def test_two_period_manifest_cannot_remove_both_period_end_declarations(self):
        manifest = test_manifest(self.events, self.payload)
        manifest["match"]["expectedPeriods"] = [1, 2]
        manifest["match"]["requiredEventCounts"] = {"HalfTime": 1, "FullTime": 1}
        manifest["match"]["requiredEventMarkers"] = {
            "HalfTime": {"clockSeconds": 2700, "period": 1},
            "FullTime": {"clockSeconds": 5761, "period": 2},
        }
        del manifest["match"]["requiredEventCounts"]["HalfTime"]
        del manifest["match"]["requiredEventMarkers"]["HalfTime"]
        with self.assertRaisesRegex(validator.CandidateValidationError, "must require exactly one HalfTime"):
            validator.validate_source(self.payload, manifest)

    def test_rejects_nonfinite_source_values(self):
        payload = b'[{"x":NaN}]'
        manifest = test_manifest(self.events, self.payload)
        manifest["source"] = {**manifest["source"], "sha256": validator.sha256(payload).hexdigest(), "byteSize": len(payload)}
        with self.assertRaisesRegex(validator.CandidateValidationError, "Non-finite JSON constant"):
            validator.validate_source(payload, manifest)

    def test_rejects_overflowing_raw_coordinates(self):
        invalid = [{**self.events[0], "pitchX": 10 ** 1000}, self.events[1]]
        payload = json.dumps(invalid).encode()
        with self.assertRaisesRegex(validator.CandidateValidationError, "JSON integer exceeds the supported size"):
            validator.validate_source(payload, test_manifest(invalid, payload))

    def test_pinned_repository_candidate_has_stable_source_integrity_report(self):
        source = ROOT / "data/simulations/manu-liv-match-dataset.json"
        manifest_path = ROOT / "data/simulations/manu-liv-candidate-manifest.json"
        first = validator.validate_file(source, manifest_path)
        second = validator.validate_file(source, manifest_path)
        self.assertEqual(first["eventCount"], 2199)
        self.assertEqual(first["attachedPositionCount"], 48048)
        self.assertEqual(first["sourceSequence"], {"first": 1, "last": 2199, "preserved": True})
        self.assertEqual(first["sourceClock"]["last"], 5761)
        self.assertEqual(first["eventCounts"], second["eventCounts"])
        self.assertEqual(first["sourceEventIdLedgerSha256"], second["sourceEventIdLedgerSha256"])
        self.assertEqual(first["orderedEventRecordsSha256"], second["orderedEventRecordsSha256"])
        self.assertEqual(first["eventCounts"]["Shot"], 124)
        self.assertEqual(first["eventCounts"]["Foul"], 254)
        self.assertTrue(first["footballReviewRequired"])
        self.assertEqual(first["independentRightsPrivacyReview"], "pending")

    def test_cli_rejects_hardlink_output_aliases_without_modifying_inputs(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            source = directory / "source.json"
            manifest = directory / "manifest.json"
            source_alias = directory / "source-output.json"
            manifest_alias = directory / "manifest-output.json"
            source.write_bytes(self.payload)
            manifest_bytes = json.dumps(self.manifest).encode("utf-8")
            manifest.write_bytes(manifest_bytes)
            source_before = source.read_bytes()

            source_alias.hardlink_to(source)
            self.assertEqual(
                validator.main([str(source), "--manifest", str(manifest), "--output", str(source_alias)]),
                2,
            )
            self.assertEqual(source.read_bytes(), source_before)

            manifest_alias.hardlink_to(manifest)
            self.assertEqual(
                validator.main([str(source), "--manifest", str(manifest), "--output", str(manifest_alias)]),
                2,
            )
            self.assertEqual(manifest.read_bytes(), manifest_bytes)


if __name__ == "__main__":
    unittest.main()
