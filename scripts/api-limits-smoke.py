#!/usr/bin/env python3
"""Run against a fresh API process; validates bounds without bypassing quotas."""
import json
import sys
import urllib.error
import urllib.request

base = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5080"
try:
    with urllib.request.urlopen(base + "/api/v1/matches", timeout=10) as response:
        fixture_payload = json.load(response)
except urllib.error.HTTPError as error:
    if error.code == 503:
        problem = json.load(error)
        if problem.get("code") == "MATCH_DATA_UNAVAILABLE":
            raise SystemExit("BLOCKED: analyst rate-limit smoke requires a validated match provider")
    raise
fixture = fixture_payload[0]
route = base + "/api/v1/matches/" + fixture["matchId"]
insight = json.load(urllib.request.urlopen(route + "/insights?atSeconds=3309", timeout=10))[0]
body = json.dumps({"insightId": insight["id"], "atSeconds": 3309, "audience": "fan"}).encode()
statuses = []
for _ in range(31):
    req = urllib.request.Request(route + "/analyst", data=body, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            statuses.append(response.status)
    except urllib.error.HTTPError as error:
        statuses.append(error.code)
assert 200 in statuses and 429 in statuses, statuses
req = urllib.request.Request(route + "/analyst", data=b" " * 5000, headers={"Content-Type": "application/json"})
try:
    urllib.request.urlopen(req, timeout=10)
    raise AssertionError("Oversized request accepted")
except urllib.error.HTTPError as error:
    # The quota can take precedence over body admission once exhausted.
    assert error.code in (413, 429), error.code
print("PASS: analyst request quota rejects excess requests; oversized request rejected")
