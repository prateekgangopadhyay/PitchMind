#!/usr/bin/env python3
"""Verify the analyst endpoint remains rate-limited while match data is unavailable.

Run against a fresh API process so this client's fixed-window quota is untouched.
"""
import json
import sys
import urllib.error
import urllib.request
import uuid

base = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5080"
route = f"{base}/api/v1/matches/{uuid.uuid4()}/analyst"
route_path = route[len(base):]
payload = json.dumps({"insightId": "smoke-check", "atSeconds": 0, "audience": "fan"}).encode()
statuses = []

for attempt in range(31):
    request = urllib.request.Request(
        route,
        data=payload,
        headers={"Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            status = response.status
            body = json.load(response)
            headers = response.headers
    except urllib.error.HTTPError as error:
        status = error.code
        headers = error.headers
        body = json.load(error) if headers.get("Content-Type", "").endswith("+json") else None

    statuses.append(status)
    assert isinstance(body["type"], str) and body["type"], body
    assert isinstance(body["title"], str) and body["title"], body
    if attempt < 30:
        assert status == 503, (attempt + 1, status, body)
        assert body["code"] == "MATCH_DATA_UNAVAILABLE", body
        assert headers.get("Content-Type", "").startswith("application/problem+json"), headers
        assert headers.get("X-Contract-Version") == "1", headers
        assert headers.get("X-Correlation-ID") == body["correlationId"], body
        assert body["status"] == 503 and body["instance"] == route_path, body
    else:
        assert status == 429, (attempt + 1, status, body)
        assert headers.get("Content-Type", "").startswith("application/problem+json"), headers
        assert headers.get("X-Correlation-ID"), headers
        assert headers.get("X-Contract-Version") == "1", headers
        assert body["code"] == "RATE_LIMITED", body
        assert body["status"] == 429, body
        assert body["correlationId"] == headers["X-Correlation-ID"], body
        assert body["instance"] == route_path, body

print("PASS: 30 analyst requests fail closed with 503; request 31 is rate-limited with 429")
