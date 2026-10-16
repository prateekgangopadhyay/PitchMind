import asyncio
from hashlib import sha256
import json
from pathlib import Path
import tempfile
import unittest

from scripts.ep03_s05_load_harness import (
    HarnessError,
    _source_records,
    build_report,
    execute,
    load_profile,
    parse_json_array_records,
    run_generator,
)


class FakeClock:
    def __init__(self) -> None:
        self.now = 100.0

    def __call__(self) -> float:
        return self.now

    async def sleep(self, delay: float) -> None:
        self.now += delay


class Ep03S05LoadHarnessTests(unittest.TestCase):
    def test_parser_preserves_record_json_bytes_and_original_identity(self) -> None:
        source = b'[ { "eventId" : "evt-A", "sequence" : 7, "matchId" : "match-X", "extra" : [1, 2] },\n{"eventId":"evt-B","sequence":8,"matchId":"match-X"} ]'
        records = parse_json_array_records(source)
        self.assertEqual([("evt-A", 7), ("evt-B", 8)], [(row.event_id, row.sequence) for row in records])
        self.assertEqual(["match-X", "match-X"], [row.match_id for row in records])
        self.assertEqual(b'{ "eventId" : "evt-A", "sequence" : 7, "matchId" : "match-X", "extra" : [1, 2] }', records[0].payload)
        self.assertEqual(b'{"eventId":"evt-B","sequence":8,"matchId":"match-X"}', records[1].payload)

    def test_parser_rejects_missing_identity_without_rewriting_or_guessing(self) -> None:
        with self.assertRaisesRegex(HarnessError, "no string event ID"):
            parse_json_array_records(b'[{"sequence":1,"matchId":"m"}]')
        with self.assertRaisesRegex(HarnessError, "integer sequence"):
            parse_json_array_records(b'[{"eventId":"e","sequence":true,"matchId":"m"}]')
        with self.assertRaisesRegex(HarnessError, "Only a top-level JSON array"):
            parse_json_array_records(b'{"eventId":"e","sequence":1,"matchId":"m"}')
        with self.assertRaisesRegex(HarnessError, "duplicate keys"):
            parse_json_array_records(b'[{"eventId":"first","eventId":"second","sequence":1,"matchId":"m"}]')

    def test_generator_repeats_source_identity_and_raw_payload_in_order(self) -> None:
        records = parse_json_array_records(
            b'[{"eventId":"evt-A","sequence":7,"matchId":"match-X"},{"eventId":"evt-B","sequence":8,"matchId":"match-X"}]'
        )
        clock = FakeClock()
        result = asyncio.run(run_generator(
            records,
            rate=2,
            duration_seconds=1.5,
            queue_capacity=2,
            clock=clock,
            sleep=clock.sleep,
        ))
        expected_identity = sha256()
        expected_payload = sha256()
        for record in (records[0], records[1], records[0]):
            expected_identity.update(record.event_id.encode())
            expected_identity.update(b"\0")
            expected_identity.update(str(record.sequence).encode())
            expected_identity.update(b"\n")
            expected_payload.update(record.payload)
            expected_payload.update(b"\n")
        self.assertTrue(result["complete"])
        self.assertEqual(3, result["scheduledRecords"])
        self.assertEqual(3, result["localSinkReceivedRecords"])
        self.assertEqual(expected_identity.hexdigest(), result["replayedEventIdSequenceSha256"])
        self.assertEqual(expected_payload.hexdigest(), result["replayedPayloadSha256"])
        self.assertEqual(2, result["queueCapacityRecords"])
        self.assertLessEqual(result["maxInMemoryQueueDepth"], 2)
        self.assertEqual(2.0, result["scheduledRatePerSecond"])

    def test_profile_pins_owner_directed_diagnostic_without_acceptance_budgets(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        profile = load_profile(repo_root / "docs/engineering/performance/ep03-s05-load-profile.json")
        self.assertEqual(5, profile["workload"]["durationSeconds"])
        self.assertIn("attached playerPositions array", profile["workload"]["payloadMixApproval"])
        self.assertEqual("stop-at-source-end", profile["workload"]["replayMode"])
        self.assertEqual(
            "owner-directed single-match partition mix (2026-10-16)",
            profile["workload"]["partitionMixApproval"],
        )
        self.assertEqual("pending", profile["agreement"]["status"])
        self.assertTrue(all(value is None for value in profile["agreement"]["acceptanceBudgets"].values()))

    def test_report_distinguishes_owner_partition_choice_from_unapproved_payload_mix(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        profile_path = repo_root / "docs/engineering/performance/ep03-s05-load-profile.json"
        profile = load_profile(profile_path)
        record = parse_json_array_records(b'[{"eventId":"evt-1","sequence":1,"matchId":"match-1"}]')[0]
        report = build_report(profile, repo_root / "data/source.json", b"[]", [record],
            {"complete": True}, duration_override=1.0, run_id="test-run", started_at="2026-10-16T00:00:00Z")
        self.assertEqual("single-source-match", report["profile"]["partitionMode"])
        self.assertEqual(profile["workload"]["partitionMixApproval"], report["profile"]["partitionMixApproval"])
        self.assertEqual(profile["workload"]["payloadMixApproval"], report["profile"]["payloadMixApproval"])
        self.assertEqual("stop-at-source-end", report["profile"]["replayMode"])
        self.assertTrue(report["source"]["attachedPlayerPositionsArePreserved"])
        self.assertEqual(0, report["source"]["standaloneFrameRecords"])

    def test_profile_rejects_non_finite_duration(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        template = json.loads((repo_root / "docs/engineering/performance/ep03-s05-load-profile.json").read_text())
        for duration in (float("nan"), float("inf"), float("-inf")):
            with self.subTest(duration=duration), tempfile.TemporaryDirectory() as directory:
                profile = json.loads(json.dumps(template))
                profile["workload"]["durationSeconds"] = duration
                profile_path = Path(directory) / "profile.json"
                profile_path.write_text(json.dumps(profile))
                with self.assertRaisesRegex(HarnessError, "durationSeconds"):
                    load_profile(profile_path)

    def test_profile_rejects_duration_above_runtime_bound(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        template = json.loads((repo_root / "docs/engineering/performance/ep03-s05-load-profile.json").read_text())
        template["workload"]["durationSeconds"] = 10**400
        with tempfile.TemporaryDirectory() as directory:
            profile_path = Path(directory) / "profile.json"
            profile_path.write_text(json.dumps(template))
            with self.assertRaisesRegex(HarnessError, "through 3600"):
                load_profile(profile_path)

    def test_execute_rejects_non_finite_duration_override_without_traceback_or_report(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        profile_path = repo_root / "docs/engineering/performance/ep03-s05-load-profile.json"
        for duration in (float("nan"), float("inf"), float("-inf")):
            with self.subTest(duration=duration), tempfile.TemporaryDirectory() as directory:
                output_path = Path(directory) / "report.json"
                with self.assertRaisesRegex(HarnessError, "Run duration"):
                    asyncio.run(execute(profile_path, output_path, duration))
                self.assertFalse(output_path.exists())

    def test_profile_rejects_multimatch_partition_mode(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        template = json.loads((repo_root / "docs/engineering/performance/ep03-s05-load-profile.json").read_text())
        template["workload"]["partitionMode"] = "round-robin-multimatch"
        with tempfile.TemporaryDirectory() as directory:
            profile_path = Path(directory) / "profile.json"
            profile_path.write_text(json.dumps(template))
            with self.assertRaisesRegex(HarnessError, "Multi-match"):
                load_profile(profile_path)

    def test_profile_rejects_nonopaque_payload_mix(self) -> None:
        repo_root = Path(__file__).resolve().parents[1]
        template = json.loads((repo_root / "docs/engineering/performance/ep03-s05-load-profile.json").read_text())
        template["workload"]["payloadMode"] = "semantic-shot-event-weighting"
        with tempfile.TemporaryDirectory() as directory:
            profile_path = Path(directory) / "profile.json"
            profile_path.write_text(json.dumps(template))
            with self.assertRaisesRegex(HarnessError, "Payload shaping"):
                load_profile(profile_path)

    def test_generator_stops_at_source_end_without_repeating_event_id_or_payload(self) -> None:
        records = parse_json_array_records(
            b'[{"eventId":"evt-A","sequence":7,"matchId":"match-X"},'
            b'{"eventId":"evt-B","sequence":8,"matchId":"match-X"}]'
        )
        clock = FakeClock()
        result = asyncio.run(run_generator(
            records,
            rate=2,
            duration_seconds=5,
            queue_capacity=2,
            stop_at_source_end=True,
            clock=clock,
            sleep=clock.sleep,
        ))
        self.assertEqual(10, result["requestedRecords"])
        self.assertEqual(2, result["plannedRecords"])
        self.assertEqual(2, result["scheduledRecords"])
        self.assertEqual(2, result["localSinkReceivedRecords"])
        self.assertTrue(result["sourceRecordLimitReached"])
        self.assertEqual(5, result["fullRunSeconds"])
        self.assertFalse(result["complete"])

    def test_source_hash_drift_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "source.json").write_bytes(b'[{"eventId":"e","sequence":1,"matchId":"m"}]')
            profile = {
                "source": {
                    "path": "source.json",
                    "expectedSha256": "0" * 64,
                    "maxBytes": 1000,
                    "maxRecords": 10,
                }
            }
            with self.assertRaisesRegex(HarnessError, "SHA-256"):
                _source_records(profile, root)

    def test_multimatch_source_is_rejected(self) -> None:
        source = b'[{"eventId":"e1","sequence":1,"matchId":"m1"},{"eventId":"e2","sequence":1,"matchId":"m2"}]'
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "source.json").write_bytes(source)
            profile = {
                "source": {
                    "path": "source.json",
                    "expectedSha256": sha256(source).hexdigest(),
                    "maxBytes": 1000,
                    "maxRecords": 10,
                    "eventIdKey": "eventId",
                    "sequenceKey": "sequence",
                    "matchIdKey": "matchId",
                }
            }
            with self.assertRaisesRegex(HarnessError, "only one source match"):
                _source_records(profile, root)


if __name__ == "__main__":
    unittest.main()
