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
        help="Verify legacy behavior where Azure DEV identity is intentionally unconfigured.",
    )
    parser.add_argument(
        "--expect-auth-required",
        action="store_true",
        help="Verify the Azure API is healthy and protected routes require a real bearer token.",
    )
    args = parser.parse_args(argv)
    base = args.base.rstrip("/")
    if args.expect_identity_unavailable and args.expect_auth_required:
        parser.error("Choose only one expected identity mode.")
    expected_code = "IDENTITY_UNAVAILABLE" if args.expect_identity_unavailable else "UNAUTHORIZED" if args.expect_auth_required else "MATCH_DATA_UNAVAILABLE"

    def request(path: str, expected: int = 200, payload: dict | None = None) -> dict:
        body = None if payload is None else json.dumps(payload).encode()
        headers = {"Content-Type": "application/json"}
        if not args.expect_identity_unavailable and not args.expect_auth_required:
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
    if args.expect_auth_required:
        # Azure release smoke must prove the private PostgreSQL endpoint and
        # current schema are reachable. Local contract smoke intentionally
        # runs without PostgreSQL and only validates the HTTP/auth contract.
        ready_req = urllib.request.Request(base + "/health/ready")
        try:
            ready_response = open_url(ready_req, timeout=10)
        except urllib.error.HTTPError as error:
            ready_response = error
        assert ready_response.status == 200, ("/health/ready", ready_response.status)
        assert ready_response.headers.get("X-Correlation-ID"), "/health/ready"
    if args.expect_auth_required:
        # The SPA is served from the application root in the container image.
        # Local API contract tests run the API project without frontend assets.
        page_req = urllib.request.Request(base + "/")
        page = open_url(page_req, timeout=10)
        assert page.status == 200, ("/", page.status)
        assert "text/html" in page.headers.get("Content-Type", ""), ("/", page.headers)
    match_routes = [
        ("/api/v1/matches", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/state", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/events", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/insights", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/evidence/test-insight", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/evidence/test-insight/replay", None),
        ("/api/v1/matches/00000000-0000-0000-0000-000000000000/analyst", {"insightId": "test-insight", "atSeconds": 0, "audience": "fan"}),
    ]
    if args.expect_auth_required:
        # Candidate-feed routes are only composed when the deployed runtime
        # has its event stream configured; DEV must expose and protect it.
        match_routes.insert(0, ("/api/v1/simulation/candidate/status", None))
    expected_status = 401 if args.expect_auth_required else 503
    for route, payload in match_routes:
        problem = request(route, expected_status, payload)
        assert problem["code"] == expected_code, (route, problem)
        assert problem["status"] == expected_status, (route, problem)
    if args.expect_identity_unavailable:
        print("PASS: health is available and protected match routes fail closed; Entra sign-in is not configured in DEV")
    elif args.expect_auth_required:
        print("PASS: application page and health are available; protected match routes reject anonymous requests")
    else:
        print("PASS: health is available and match routes fail closed until canonical route composition is implemented")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
