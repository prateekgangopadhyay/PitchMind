#!/usr/bin/env python3
"""Verify health and fail-closed match routes; requires a running API."""
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
    if path.startswith("/api/v1/"):
        assert response.headers.get("X-Contract-Version") == "1", path
    return json.load(response)


request("/api/health")
match_routes = [
    ("/api/v1/matches", None),
    ("/api/v1/matches/00000000-0000-0000-0000-000000000000/state", None),
    ("/api/v1/matches/00000000-0000-0000-0000-000000000000/events", None),
    ("/api/v1/matches/00000000-0000-0000-0000-000000000000/insights", None),
    ("/api/v1/matches/00000000-0000-0000-0000-000000000000/evidence/test-insight", None),
    ("/api/v1/matches/00000000-0000-0000-0000-000000000000/analyst", {"insightId": "test-insight", "atSeconds": 0}),
]
for route, payload in match_routes:
    problem = request(route, 503, payload)
    assert problem["code"] == "MATCH_DATA_UNAVAILABLE", (route, problem)
    assert problem["status"] == 503, (route, problem)
print("PASS: health is available and match reads return 503 until canonical route composition is implemented")
