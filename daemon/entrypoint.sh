#!/usr/bin/env bash
set -Eeuo pipefail

_on_error() {
  trap '' ERR
  line_path=$(caller)
  line=${line_path% *}
  path=${line_path#* }

  echo ""
  echo "ERR $path:$line $BASH_COMMAND exited with $1"
  exit 1
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
    case "$KRYOLITE__DAEMON__CLEAN" in
      blocks|wallet)
        if [[ -f "data/${KRYOLITE__DAEMON__CLEAN}.dat" ]]
        then
          rm "data/${KRYOLITE__DAEMON__CLEAN}.dat"
          echo "removed data/${KRYOLITE__DAEMON__CLEAN}.dat"
        else
          echo "no data/${KRYOLITE__DAEMON__CLEAN}.dat to remove"
        fi
      ;;
      all)
        rm -rf data/*
        echo "cleaned all data"
      ;;
      none)
        :
      ;;
      *)
        echo "unknown value in KRYOLITE__DAEMON__CLEAN: $KRYOLITE__DAEMON__CLEAN"
        exit 1
      ;;
    esac

    while true; do
      [[ -f "/build/daemon" ]] && break
      echo "waiting for /build/daemon to appear"
      sleep 0.5
    done

    daemonPid=""
    while true; do
      touch "/build/DAEMON.$HOSTNAME"

      echo "starting /build/daemon in $(pwd)"
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
