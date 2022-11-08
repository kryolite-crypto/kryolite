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

subcommand="${1:-}"
case "${subcommand}" in
  hang)
    tail -f /dev/null & wait
  ;;
esac

#dotnet build

#exec reflex -v -s -r '\.cs$' dotnet run
exec reflex -s -r '\.cs$' -- dotnet run --data-dir=/data bin/Debug/net7.0/daemon
