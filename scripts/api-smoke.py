#!/usr/bin/env python3
"""Verify API health and protected fail-closed match routes."""
import argparse
import json
import sys
import urllib.error
import urllib.request


def main(argv: list[str] | None = None, open_url=urllib.request.urlopen) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base", nargs="?", default="http://127.0.0.1:5080")
    parser.add_argument(
        "--expect-identity-unavailable",
        action="store_true",
        help="Azure DEV has no Entra API audience configured yet; verify protected routes fail closed.",
    )
    args = parser.parse_args(argv)
    base = args.base.rstrip("/")
    expected_code = "IDENTITY_UNAVAILABLE" if args.expect_identity_unavailable else "MATCH_DATA_UNAVAILABLE"

    def request(path: str, expected: int = 200, payload: dict | None = None) -> dict:
        body = None if payload is None else json.dumps(payload).encode()
        headers = {"Content-Type": "application/json"}
        if not args.expect_identity_unavailable:
            headers["X-Dev-Subject"] = "api-smoke"
            headers["X-Dev-Roles"] = "analyst"
        req = urllib.request.Request(base + path, data=body, headers=headers)
        try:
            response = open_url(req, timeout=10)
        except urllib.error.HTTPError as error:
            response = error
        assert response.status == expected, (path, response.status, expected)
        assert response.headers.get("X-Correlation-ID"), path
        if path.startswith("/api/v1/"):
            assert response.headers.get("X-Contract-Version") == "1", path
        response_body = json.load(response)
        if expected >= 400:
            assert response.headers.get("Content-Type", "").startswith("application/problem+json"), (path, response.headers)
            assert isinstance(response_body["type"], str) and response_body["type"], (path, response_body)
            assert isinstance(response_body["title"], str) and response_body["title"], (path, response_body)
            assert response_body["status"] == expected, (path, response_body)
            assert response_body["code"] == expected_code, (path, response_body)
            assert response_body["correlationId"] == response.headers["X-Correlation-ID"], (path, response_body)
            assert response_body["instance"] == path, (path, response_body)
        return response_body

    request("/api/health")
    match_routes = [
        ("/api/v1/matches", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/state", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/events", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/insights", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/evidence/test-insight", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/evidence/test-insight/replay", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/analyst", {"insightId": "test-insight", "atSeconds": 0, "audience": "fan"}),
    ]
    for route, payload in match_routes:
        problem = request(route, 503, payload)
        assert problem["code"] == expected_code, (route, problem)
        assert problem["status"] == 503, (route, problem)
    if args.expect_identity_unavailable:
        print("PASS: health is available and protected match routes fail closed; Entra sign-in is not configured in DEV")
    else:
        print("PASS: health is available and match routes fail closed until canonical route composition is implemented")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
