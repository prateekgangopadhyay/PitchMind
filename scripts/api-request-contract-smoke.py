#!/usr/bin/env python3
"""Verify analyst request validation and bounded-body errors against a fresh local API."""
import http.client
import json
import sys
import urllib.error
import urllib.request
import uuid

base = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5080"
path = f"/api/v1/matches/{uuid.uuid4()}/analyst"


def request(body, expected, code, content_type="application/json"):
    req = urllib.request.Request(
        base + path,
        data=body,
        headers={"Content-Type": content_type},
    )
    try:
        response = urllib.request.urlopen(req, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    problem = json.load(response)
    assert response.status == expected, (response.status, problem)
    assert response.headers.get("Content-Type", "").startswith("application/problem+json"), response.headers
    assert response.headers.get("X-Contract-Version") == "1", response.headers
    assert response.headers.get("X-Correlation-ID") == problem["correlationId"], problem
    assert problem["status"] == expected and problem["code"] == code, problem
    return problem


request(b"{", 400, "INVALID_REQUEST")
request(b'{"insightId":"i","atSeconds":5401,"audience":"fan"}', 400, "INVALID_REQUEST")
request(b'{"insightId":"i","atSeconds":0,"audience":"fan"}', 415,
        "UNSUPPORTED_MEDIA_TYPE", "text/plain")
request(b'{"insightId":"' + b"x" * 5000 + b'","atSeconds":0,"audience":"fan"}',
        413, "REQUEST_TOO_LARGE")

connection = http.client.HTTPConnection("127.0.0.1", int(base.rsplit(":", 1)[1]), timeout=10)
chunk = b'{"insightId":"' + b"x" * 5000 + b'","atSeconds":0,"audience":"fan"}'
connection.putrequest("POST", path)
connection.putheader("Content-Type", "application/json")
connection.putheader("Transfer-Encoding", "chunked")
connection.endheaders()
connection.send(f"{len(chunk):X}\r\n".encode() + chunk + b"\r\n0\r\n\r\n")
response = connection.getresponse()
problem = json.loads(response.read())
assert response.status == 413, (response.status, problem)
assert response.getheader("Content-Type", "").startswith("application/problem+json"), response.getheaders()
assert response.getheader("X-Contract-Version") == "1", response.getheaders()
assert response.getheader("X-Correlation-ID") == problem["correlationId"], problem
assert problem["status"] == 413 and problem["code"] == "REQUEST_TOO_LARGE", problem
connection.close()

request(b'{"insightId":"i","atSeconds":5400,"audience":"analyst"}',
        503, "MATCH_DATA_UNAVAILABLE")
print("PASS: malformed/invalid request=400, unsupported media=415, oversized content-length/chunked=413, valid request fails closed=503")
