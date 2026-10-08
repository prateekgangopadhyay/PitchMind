#!/usr/bin/env bash
set -euo pipefail

api_url="${1:-http://127.0.0.1:5080}"
if [[ ! "$api_url" =~ ^http://(127\.0\.0\.1|localhost)(:[0-9]+)?$ ]]; then
  echo "API URL must be a loopback base URL: http://127.0.0.1[:port] or http://localhost[:port]" >&2
  exit 2
fi

curl --fail --silent --show-error --max-time 3 "${api_url%/}/health" >/dev/null || {
  echo "API is not ready at ${api_url%/}/health" >&2
  exit 1
}
echo "PASS: API readiness endpoint healthy (LOCAL)"
