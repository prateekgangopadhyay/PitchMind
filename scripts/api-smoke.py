#!/usr/bin/env python3
"""Real HTTP selected-time and negative-contract checks; requires a running API."""
import json
import sys
import urllib.error
import urllib.request

base = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5080"


def request(path, expected=200, payload=None):
    body = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(base + path, data=body, headers={"Content-Type": "application/json"})
    try:
        response = urllib.request.urlopen(req, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    assert response.status == expected, (path, response.status, expected)
    assert response.headers.get("X-Correlation-ID"), path
    return json.load(response)


fixture = request("/api/v1/matches")[0]
assert fixture["provenance"] == "synthetic"
match = "/api/v1/matches/" + fixture["matchId"]
before = request(match + "/state?atSeconds=3308")
after = request(match + "/state?atSeconds=3309")
assert (before["homeScore"], after["homeScore"]) == (0, 1)
assert request(match + "/insights?atSeconds=3308") == []
insight = request(match + "/insights?atSeconds=3309")[0]
evidence_path = match + "/evidence/" + insight["id"]
request(evidence_path + "?atSeconds=3308", 404)
evidence = request(evidence_path + "?atSeconds=3309")
assert len(evidence["events"]) == 7
assert all(e["event"]["clockSeconds"] <= 3309 for e in evidence["events"])
answer = request(match + "/analyst", payload={"insightId": insight["id"], "atSeconds": 3309, "audience": "fan"})
assert answer["source"] == "deterministic-fallback"
assert answer["eventIds"] == insight["eventIds"]
request(match + "/analyst", 400, {"insightId": insight["id"], "atSeconds": 3309, "audience": "invalid"})
request(match + "/state?atSeconds=-1", 400)
request(match + "/state?atSeconds=5401", 400)
request("/api/v1/matches/00000000-0000-0000-0000-000000000000/state", 404)
final = request(match + "/state?atSeconds=5400")
assert (final["homeScore"], final["awayScore"], final["status"]) == (1, 1, "full-time")
print("PASS: real HTTP replay, score boundary, evidence, fallback, invalid audience/time and unknown match")
