#!/usr/bin/env python3
"""Verify API health and protected fail-closed match routes."""
import argparse
import json
import os
import ssl
import sys
import time
import urllib.error
import urllib.request


def _default_ssl_context() -> ssl.SSLContext | None:
    configured_bundle = (
        os.environ.get("PITCHMIND_CA_BUNDLE")
        or os.environ.get("SSL_CERT_FILE")
        or os.environ.get("REQUESTS_CA_BUNDLE")
    )
    if configured_bundle:
        if not os.path.isfile(configured_bundle):
            raise FileNotFoundError(f"Configured CA bundle does not exist: {configured_bundle}")
        return ssl.create_default_context(cafile=configured_bundle)

    ca_candidates = [
        "/etc/ssl/cert.pem",
        "/etc/pki/tls/certs/ca-bundle.crt",
        "/etc/ssl/certs/ca-certificates.crt",
    ]
    try:
        import certifi
        ca_candidates.insert(0, certifi.where())
    except ImportError:
        pass
    for candidate in ca_candidates:
        if os.path.exists(candidate):
            try:
                return ssl.create_default_context(cafile=candidate)
            except Exception:
                pass
    return None


def _default_open_url(request: urllib.request.Request, timeout: int = 10):
    try:
        return urllib.request.urlopen(request, timeout=timeout)
    except urllib.error.URLError as error:
        reason = getattr(error, "reason", None)
        if isinstance(reason, ssl.SSLCertVerificationError) or "CERTIFICATE_VERIFY_FAILED" in str(error):
            try:
                fallback_context = _default_ssl_context()
            except (OSError, ssl.SSLError) as context_error:
                raise urllib.error.URLError(
                    f"Could not load the configured CA bundle: {context_error}. Set PITCHMIND_CA_BUNDLE "
                    "on the self-hosted agent to a readable PEM file containing the trusted CA chain."
                ) from error
            if fallback_context is not None:
                try:
                    return urllib.request.urlopen(request, timeout=timeout, context=fallback_context)
                except urllib.error.URLError as fallback_error:
                    fallback_reason = getattr(fallback_error, "reason", fallback_error)
                    if not (
                        isinstance(fallback_reason, ssl.SSLCertVerificationError)
                        or "CERTIFICATE_VERIFY_FAILED" in str(fallback_error)
                    ):
                        raise
                    raise urllib.error.URLError(
                        f"{fallback_reason}. Certificate validation remains enabled. Configure "
                        "PITCHMIND_CA_BUNDLE on the self-hosted agent with the trusted CA chain "
                        "used for outbound HTTPS, then restart the agent service."
                    ) from fallback_error
            raise urllib.error.URLError(
                f"{reason}. Certificate validation remains enabled; configure PITCHMIND_CA_BUNDLE "
                "on the self-hosted agent with the trusted CA chain used for outbound HTTPS."
            ) from error
        raise


def main(argv: list[str] | None = None, open_url=_default_open_url, sleep=time.sleep) -> int:
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
        ready_response = None
        for attempt in range(15):
            try:
                ready_response = open_url(ready_req, timeout=10)
            except (urllib.error.HTTPError, urllib.error.URLError) as error:
                ready_response = error
            if getattr(ready_response, "status", None) == 200:
                break
            if attempt < 14:
                sleep(2)
        assert ready_response is not None and getattr(ready_response, "status", None) == 200, (
            "/health/ready",
            getattr(ready_response, "status", None),
        )
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
