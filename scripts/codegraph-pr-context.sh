#!/usr/bin/env bash
# Offline advisory blast-radius / test-gap analysis of one PR against its actual parent.
# Never writes to the repository, opens a PR, contacts Azure, or approves a merge.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ $# -ne 1 ]]; then
  echo "Usage: scripts/codegraph-pr-context.sh <exact-local-base-ref>" >&2
  echo "For stacked PRs, pass the checked-out parent's exact local ref or commit, not origin/main." >&2
  exit 2
fi
BASE="$1"
if ! git rev-parse --verify --quiet "${BASE}^{commit}" >/dev/null; then
  echo "Base ref '$BASE' is unavailable. Fetch the PR's real parent ref first." >&2
  exit 2
fi
if ! git merge-base --is-ancestor "$BASE" HEAD; then
  echo "Base '$BASE' is not an ancestor of HEAD; inspect/restack the PR before graph analysis." >&2
  exit 2
fi

if [[ -n "${CODEGRAPH_SERVER_PATH:-}" ]]; then
  BIN="$CODEGRAPH_SERVER_PATH"
elif command -v codegraph-server >/dev/null 2>&1; then
  BIN="$(command -v codegraph-server)"
else
  case "$(uname -s)" in
    Linux) platform=linux ;;
    Darwin) platform=darwin ;;
    *) echo "Unsupported OS; set CODEGRAPH_SERVER_PATH to the CodeGraph analysis engine." >&2; exit 2 ;;
  esac
  case "$(uname -m)" in
    x86_64|amd64) arch=x64 ;;
    aarch64|arm64) arch=arm64 ;;
    *) echo "Unsupported architecture; set CODEGRAPH_SERVER_PATH." >&2; exit 2 ;;
  esac
  if ! command -v npm >/dev/null 2>&1; then
    echo "npm is not installed. Install @astudioplus/codegraph-mcp or set CODEGRAPH_SERVER_PATH." >&2
    exit 2
  fi
  BIN="$(npm root -g)/@astudioplus/codegraph-mcp/bin/codegraph-server-${platform}-${arch}"
fi

if [[ ! -x "$BIN" ]]; then
  echo "CodeGraph engine not executable at '$BIN'." >&2
  echo "Opt-in installation: npm install -g @astudioplus/codegraph-mcp" >&2
  echo "No PR review or merge is blocked by missing CodeGraph; run normal tests/review instead." >&2
  exit 2
fi
if ! command -v node >/dev/null 2>&1; then
  echo "Node.js is required to safely encode the base ref." >&2
  exit 2
fi
ARGS="$(node -e 'process.stdout.write(JSON.stringify({baseBranch:process.argv[1],format:"markdown"}))' "$BASE")"
"$BIN" --workspace "$ROOT" --profile graph --graph-only \
  --run-tool codegraph_pr_context --tool-args "$ARGS"
