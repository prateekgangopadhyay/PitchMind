#!/usr/bin/env python3
"""Verify analyst request validation and bounded-body errors against a fresh local API."""
import http.client
import json
import sys
import urllib.error
import urllib.request
import uuid
from urllib.parse import urlsplit

base = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5080"
path = f"/api/v1/matches/{uuid.uuid4()}/analyst"


def request(body, expected, code, content_type="application/json"):
    if content_type is None:
        target = urlsplit(base)
        connection = http.client.HTTPConnection(target.hostname, target.port, timeout=10)
        connection.request("POST", path, body=body, headers={
            "X-Dev-Subject": "api-contract-smoke",
            "X-Dev-Roles": "analyst",
        })
        response = connection.getresponse()
        status = response.status
        headers = response.headers
        payload = response.read()
        connection.close()
        problem = json.loads(payload)
        assert status == expected, (status, problem)
    else:
        req = urllib.request.Request(
            base + path,
            data=body,
            headers={
                "Content-Type": content_type,
                "X-Dev-Subject": "api-contract-smoke",
                "X-Dev-Roles": "analyst",
            },
        )
        try:
            response = urllib.request.urlopen(req, timeout=10)
        except urllib.error.HTTPError as error:
            response = error
        problem = json.load(response)
        headers = response.headers
        assert response.status == expected, (response.status, problem)
    assert headers.get("Content-Type", "").startswith("application/problem+json"), headers
    assert headers.get("X-Contract-Version") == "1", headers
    assert headers.get("X-Correlation-ID") == problem["correlationId"], problem
    assert isinstance(problem["type"], str) and problem["type"], problem
    assert isinstance(problem["title"], str) and problem["title"], problem
    assert problem["status"] == expected and problem["code"] == code, problem
    assert problem["instance"] == path, problem
    return problem


request(b"{", 400, "INVALID_REQUEST")
invalid_requests = [
    (b"[]", "INVALID_REQUEST"),
    (b'{"insightId":"","atSeconds":0,"audience":"fan"}', "INVALID_REQUEST"),
    (b'{"insightId":"   ","atSeconds":0,"audience":"fan"}', "INVALID_REQUEST"),
    (b'{"insightId":7,"atSeconds":0,"audience":"fan"}', "INVALID_REQUEST"),
    (b'{"insightId":"i","audience":"fan"}', "INVALID_REQUEST"),
    (b'{"insightId":"i","atSeconds":"0","audience":"fan"}', "INVALID_REQUEST"),
    (b'{"insightId":"i","atSeconds":1.5,"audience":"fan"}', "INVALID_REQUEST"),
    (b'{"insightId":"i","atSeconds":-1,"audience":"fan"}', "INVALID_TIME"),
    (b'{"insightId":"i","atSeconds":5401,"audience":"fan"}', "INVALID_TIME"),
    (b'{"insightId":"i","atSeconds":2147483648,"audience":"fan"}', "INVALID_TIME"),
    (b'{"insightId":"i","atSeconds":9999999999999999999999999999999999999999,"audience":"fan"}', "INVALID_TIME"),
    (b'{"insightId":"i","atSeconds":0,"audience":"Fan"}', "INVALID_REQUEST"),
    (b'{"insightId":"i","atSeconds":0,"audience":1}', "INVALID_REQUEST"),
]
for body, code in invalid_requests:
    request(body, 400, code)
request(b'{"insightId":"i","atSeconds":0,"audience":"fan"}', 415,
        "UNSUPPORTED_MEDIA_TYPE", "text/plain")
request(b'{"insightId":"i","atSeconds":0,"audience":"fan"}', 415,
        "UNSUPPORTED_MEDIA_TYPE", None)

# The configured Kestrel limit is inclusive: exactly 4096 bytes is processed,
# while a larger request is rejected with a typed 413 response.
prefix = b'{"insightId":"i","atSeconds":5400,"audience":"fan","padding":"'
suffix = b'"}'
padding_length = 4096 - len(prefix) - len(suffix)
assert padding_length >= 0
request(prefix + b"x" * padding_length + suffix, 503, "MATCH_DATA_UNAVAILABLE")
request(b'{"insightId":"' + b"x" * 5000 + b'","atSeconds":0,"audience":"fan"}',
        413, "REQUEST_TOO_LARGE")

connection = http.client.HTTPConnection("127.0.0.1", int(base.rsplit(":", 1)[1]), timeout=10)
chunk = b'{"insightId":"' + b"x" * 5000 + b'","atSeconds":0,"audience":"fan"}'
connection.putrequest("POST", path)
connection.putheader("Content-Type", "application/json")
connection.putheader("X-Dev-Subject", "api-contract-smoke")
connection.putheader("X-Dev-Roles", "analyst")
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
assert problem["instance"] == path, problem
connection.close()

request(b'{"insightId":"i","atSeconds":5400,"audience":"analyst"}',
        503, "MATCH_DATA_UNAVAILABLE")
request(b'{"insightId":"i","atSeconds":0}', 503, "MATCH_DATA_UNAVAILABLE")
print("PASS: malformed/invalid Analyst matrix=400, unsupported media=415, exact 4096-byte request=503, oversized content-length/chunked=413, valid boundary request fails closed=503")
