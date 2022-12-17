#!/usr/bin/env bash
set -Eeuo pipefail

_on_error() {
  set +x

  trap '' ERR
  line_path=$(caller)
  line=${line_path% *}
  path=${line_path#* }

  echo ""
  echo "ERR $path:$line $BASH_COMMAND exited with $1"
  exit 1
}
trap '_on_error $?' ERR

export COMPONENT=$1
export VARIANT=$2

export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto}
export DOCKER_BUILDKIT=1

RUNTIME=$VARIANT
case "$VARIANT" in
  mac-x64)
    RUNTIME=osx.11.0-x64
  ;;
  mac-arm64)
    RUNTIME=osx.11.0-arm64
  ;;
esac
export RUNTIME

docker pull "ghcr.io/${GITHUB_REPOSITORY}/builder:base" || true
docker-compose -f docker-compose.builder.yml build base

docker pull "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}" || true
docker-compose -f docker-compose.builder.yml build "${COMPONENT}"

echo "BUILD OK"
