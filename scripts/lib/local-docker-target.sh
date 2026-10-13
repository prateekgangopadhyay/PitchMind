#!/usr/bin/env bash

# Local bootstrap/maintenance scripts must never target a remote Docker daemon.
require_local_docker_target() {
  command -v docker >/dev/null || {
    echo "Docker CLI not installed" >&2
    return 1
  }

  local endpoint context
  if [[ -n "${DOCKER_CONTEXT:-}" ]]; then
    context="$DOCKER_CONTEXT"
    endpoint="$(docker context inspect "$context" --format '{{(index .Endpoints "docker").Host}}')" || {
      echo "Unable to inspect the selected Docker context; refusing local service operations." >&2
      return 1
    }
  elif [[ -n "${DOCKER_HOST:-}" ]]; then
    endpoint="$DOCKER_HOST"
  else
    context="$(docker context show)" || {
      echo "Unable to identify the selected Docker context; refusing local service operations." >&2
      return 1
    }
    endpoint="$(docker context inspect "$context" --format '{{(index .Endpoints "docker").Host}}')" || {
      echo "Unable to inspect the selected Docker context; refusing local service operations." >&2
      return 1
    }
  fi

  # Local setup, teardown, reset and health checks support Docker Engine sockets only.
  [[ "$endpoint" == unix:///* ]] || {
    echo "Refusing non-local Docker endpoint; select a local Unix-socket Docker context before using local services." >&2
    return 1
  }
}
