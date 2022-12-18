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
export DIST=$2
export COMPONENTS=${*:3}

if [[ "$COMPONENTS" = "" ]]
then
  COMPONENTS="daemon miner wallet kryolite"
fi

export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto}
export DOCKER_BUILDKIT=1

declare -A pids
for COMPONENT in $COMPONENTS; do
  (
    id=$(docker create "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}")

    output=$(mktemp -d)

    case "$VARIANT" in
      win-*)
        case "$COMPONENT" in
          kryolite)
            docker cp "$id:/build/kryolite.exe" "$output"
          ;;
          *)
            docker cp "$id:/build/${COMPONENT}.exe" "$output/kryolite-${COMPONENT}.exe"
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
  ) 2>&1 | sed -le "s#^#${COMPONENT}: #;" &
  pids[$COMPONENT]=$!
done

declare -A statuses
for COMPONENT in "${!pids[@]}"; do
  pid=${pids[$COMPONENT]}

  trap '' ERR
  set +e
    wait "$pid"
    code=$?
  set -e
  trap '_on_error $?' ERR

  if [[ "$code" -eq 0 ]]; then
    statuses[$COMPONENT]=ok
  else
    statuses[$COMPONENT]=fail
  fi
done

failed=no
for COMPONENT in "${!statuses[@]}"; do
  status=${statuses[$COMPONENT]}
  echo "component $COMPONENT package ${status}"

  [[ "$status" == "fail" ]] && failed=yes
done

if [[ "$failed" == "yes" ]]
then
  echo "PACKAGE FAILED"
  exit 1
else
  echo "PACKAGE OK"
fi
