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
  wallet)
    (
      exec tailer /tmp/xvfb.log:xvfb /tmp/x11vnc.log:x11vnc /tmp/fluxbox.log /tmp/holvi-wallet.log
    ) &

    (
      exec Xvfb :0 -listen tcp
    ) >/tmp/xvfb.log 2>&1 &

    while true; do
      echo "waiting for 127.0.0.1:6000"
      nc -z 127.0.0.1 6000 && break
      sleep 0.1
      echo "waiting for x"
    done

    echo "x ready"

    (
      exec fluxbox
    ) >/tmp/fluxbox.log 2>&1 &

    (
      exec x11vnc -listen 0.0.0.0 -shared -passwd "secret" -loop0
    ) >/tmp/x11vnc.log 2>&1 &

    (
      exec dotnet run
    ) >/tmp/holvi-wallet.log 2>&1 &

    exec tail -f /dev/null
  ;;
esac
