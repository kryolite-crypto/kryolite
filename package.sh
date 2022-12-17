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

id=$(docker create "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}")

output=$(mktemp -d)

case "$VARIANT" in
  win-*)
    case "$COMPONENT" in
      kryolite)
        docker cp "$id:/usr/local/bin/kryolite.exe" "$output"
      ;;
      *)
        docker cp "$id:/usr/local/bin/kryolite-${COMPONENT}.exe" "$output"
      ;;
    esac
  ;;
  *)
    case "$COMPONENT" in
      kryolite)
        docker cp "$id:/usr/local/bin/kryolite" "$output"
      ;;
      *)
        docker cp "$id:/usr/local/bin/kryolite-${COMPONENT}" "$output"
      ;;
    esac
  ;;
esac

docker rm "$id"

zip -jpr "${DIST}/kryolite-${COMPONENT}-${VARIANT}.zip" "$output"

echo "PACKAGE OK"
