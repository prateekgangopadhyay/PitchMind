#!/usr/bin/env bash

require_local_docker_target() {
  local endpoint context
  command -v docker >/dev/null || {
    echo "Docker CLI not installed" >&2
    return 1
  }

  if [[ -n "${DOCKER_CONTEXT:-}" ]]; then
    context="$DOCKER_CONTEXT"
    endpoint="$(docker context inspect "$context" --format '{{(index .Endpoints "docker").Host}}')" || {
      echo "Unable to inspect the selected Docker context; refusing to operate on local services." >&2
      return 1
    }
  elif [[ -n "${DOCKER_HOST:-}" ]]; then
    endpoint="$DOCKER_HOST"
  else
    context="$(docker context show)" || {
      echo "Unable to identify the selected Docker context; refusing to operate on local services." >&2
      return 1
    }
    endpoint="$(docker context inspect "$context" --format '{{(index .Endpoints "docker").Host}}')" || {
      echo "Unable to inspect the selected Docker context; refusing to operate on local services." >&2
      return 1
    }
  fi

  # LOCAL bootstrap commands may send credentials or remove data. Only allow a
  # local Unix socket; TCP, SSH, and all other Docker endpoints are rejected.
  [[ "$endpoint" == unix:///* ]] || {
    echo "Refusing non-local Docker endpoint; select a local Unix-socket Docker context before operating on local services." >&2
    return 1
  }
}
