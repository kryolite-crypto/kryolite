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

_echoerr() {
  1>&2 echo "$*"
}

_err() {
  _echoerr "err: $*"
  exit 1
}

_log() {
  _echoerr "-- ${SECONDS}s -- $*"
}

_log "export:"
export

export VARIANT=$1
export COMPONENTS=${*:2}

if [[ "$COMPONENTS" == "" ]]
then
  COMPONENTS="daemon miner wallet kryolite"
fi

export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto/kryolite}
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

case "$GITHUB_REF_TYPE" in
  tag)
    VERSION=$GITHUB_REF_NAME
    INFORMATIONAL_VERSION="$GITHUB_REF_NAME"
  ;;
  branch)
    VERSION=0.0.0
    INFORMATIONAL_VERSION="$GITHUB_REF_NAME"
  ;;
  *)
    _err "GITHUB_REF_TYPE='$GITHUB_REF_TYPE' is unknown"
  ;;
esac
export VERSION
export INFORMATIONAL_VERSION

_log "pulling ghcr.io/${GITHUB_REPOSITORY}/builder:base"
docker pull "ghcr.io/${GITHUB_REPOSITORY}/builder:base" || true

_log "building base"
docker-compose -f docker-compose.builder.yml build base

_log "building $COMPONENTS"
declare -A pids
for COMPONENT in $COMPONENTS
do
  (
    docker pull "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-${VARIANT}" || true
    docker-compose -f docker-compose.builder.yml build --progress plain "${COMPONENT}"
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
  _log "component $COMPONENT build ${status}"

  [[ "$status" == "fail" ]] && failed=yes
done

if [[ "$failed" == "yes" ]]
then
  _log "BUILD FAILED"
  exit 1
else
  _log "BUILD OK"
fi
