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

output=$(mktemp -d)
id=$(docker create "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}")

case "$VARIANT" in
  win-*)
    docker cp "$id:/build/${COMPONENT}.exe" "$output/kryolite-${COMPONENT}.exe"
  ;;
  *)
    docker cp "$id:/build/${COMPONENT}" "$output/kryolite-${COMPONENT}"
  ;;
esac

docker rm "$id"

zip -jpr "${DIST}/kryolite-${COMPONENT}-${VARIANT}.zip" "$output"

echo "PACKAGE OK"
