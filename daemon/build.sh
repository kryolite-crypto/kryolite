#!/usr/bin/env bash
set -Eeuo pipefail

_on_error() {
  trap '' ERR
  line_path=$(caller)
  line=${line_path% *}
  path=${line_path#* }

  echo ""
  echo "ERR $path:$line $BASH_COMMAND exited with $1"
  exit
}
trap '_on_error $?' ERR

_shutdown() {
  trap '' TERM INT ERR

  kill 0
  wait
  exit 0
}

trap _shutdown TERM INT ERR

timestamp=$(date)

touch "/build/EXIT"
while true; do
  if ! ls "/build/DAEMON.*" | grep "DAEMON"; then
    break
  fi

  echo "waiting for daemons to exist ..."
  sleep 0.1
done

echo "building ..."
if dotnet build -o /build; then
  echo "${timestamp}" > /build/VERSION
fi

rm "/build/EXIT"
