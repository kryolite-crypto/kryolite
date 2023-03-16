#!/usr/bin/env bash
set -Eeuo pipefail

# shellcheck disable=SC2317
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

ok=no
for ((i=1;i<=3;i++))
do
  echo "push $i / 3 - $1"
  if docker push "$1"
  then
    ok=yes
    break
  fi

  sleep 1
done

if [[ "$ok" == "yes" ]]
then
  echo "push OK with $1"
  exit 0
else
  echo "push FAIL with $1"
  exit 1
fi
