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

export VARIANT=$1
export COMPONENTS=${*:2}

if [[ "$COMPONENTS" = "" ]]
then
  COMPONENTS="daemon miner"
fi

export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto}
export DOCKER_BUILDKIT=1

case $VARIANT in
  linux-arm64)
    PLATFORM=arm64
  ;;
  linux-x64)
    PLATFORM=amd64
  ;;
  *)
    echo "unsupported docker variant: $VARIANT"
    exit 1
  ;;
esac
export PLATFORM

# NOTE: not safe for parallel on single machine due to COPY
for COMPONENT in $COMPONENTS; do
  docker pull "ghcr.io/${GITHUB_REPOSITORY}/$COMPONENT:latest" || true

  id=$(docker create "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}")
  docker cp "${id}:/usr/local/bin/kryolite-${COMPONENT}" "kryolite-${COMPONENT}"
  docker-compose -f docker-compose.release.yml build "$COMPONENT"
  rm "kryolite-${COMPONENT}"
  docker rm "$id"
done
