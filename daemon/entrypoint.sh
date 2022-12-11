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
  builder)
    if [[ -d "obj" ]]; then
      if rm -rf obj; then
        echo "removed 'obj' directory"
      else
        echo "failed to remove 'obj' directory"
        sleep 3
        exit 1
      fi
    fi

    exec reflex -v -s \
      -r '\.cs$' \
      -R "^obj\/" \
      -- \
      ./build.sh
  ;;
  daemon)
    while true; do
      [[ -f "/build/daemon" ]] && break
      echo "waiting for /build/daemon to appear"
      sleep 0.5
    done

    daemonPid=""
    while true; do
      touch "/build/DAEMON.$HOSTNAME"

      echo "starting /build/daemon"
      (
        exec /build/daemon
      ) &
      daemonPid=$!

      while true; do
        [[ -f "/build/EXIT" ]] && break
        sleep 0.1
      done

      echo "killing pid '$daemonPid' ..."
      if kill "$daemonPid"; then
        echo "... ok"
      else
        echo "... failed"
      fi

      rm "/build/DAEMON.$HOSTNAME"

      while true; do
        [[ -f "/build/EXIT" ]] || break
        echo "waiting for /build/EXIT to disappear"
        sleep 0.5
      done
    done
  ;;
  *)
    echo "häh?"
    exit 1
  ;;
esac
