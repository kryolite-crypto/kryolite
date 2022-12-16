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
    echo "HANG"
    tail -f /dev/null & wait
  ;;
  miner)
    while true; do
      dotnet run --url http://daemons:5000 --address FIM0xA101CFBF69818C624A03AF8C8FDD9B345896EE1215287EABA4CB || true
      echo ""
      echo "miner exited!"
      sleep 1
    done
  ;;
esac
