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
export DIST=$3

export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto}
export DOCKER_BUILDKIT=1

runtime=$VARIANT
case "$VARIANT" in
  mac-x64)
    runtime=osx.11.0-x64
  ;;
  mac-arm64)
    runtime=osx.11.0-arm64
  ;;
esac

set -x
docker pull "ghcr.io/${GITHUB_REPOSITORY}/builder:base" || true
docker build \
  -f Dockerfile.builder \
  --build-arg BUILDKIT_INLINE_CACHE=1 \
  --cache-from="ghcr.io/${GITHUB_REPOSITORY}/builder:base" \
  -t "ghcr.io/${GITHUB_REPOSITORY}/builder:base" .

docker pull "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}" || true
docker build \
  -f "Dockerfile.builder.${COMPONENT}" \
  --build-arg BUILDKIT_INLINE_CACHE=1 \
  --build-arg GITHUB_REPOSITORY="$GITHUB_REPOSITORY" \
  --cache-from="ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}" \
  --build-arg RUNTIME="$runtime" \
  -t "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}" .

output=$(mktemp -d)
id=$(docker create "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}")

case "$VARIANT" in
  win-*)
    docker cp "$id:/build/${COMPONENT}.exe" "$output"
  ;;
  *)
    docker cp "$id:/build/${COMPONENT}" "$output"
  ;;
esac

docker rm "$id"

zip -jpr "${DIST}/${COMPONENT}-${VARIANT}.zip" "$output"
