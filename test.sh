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

  docker-compose -f docker-compose.builder.yml logs daemon
  exit 1
}
trap '_on_error $?' ERR

_fatal() {
  echo "FATAL $*"
  kill $$
}

_cleanup() {
  docker-compose -f docker-compose.builder.yml down --remove-orphans -v -t 0
}

_balance() {
  docker-compose -f docker-compose.builder.yml exec -T daemon curl --silent "http://localhost:5000/balance?wallet=$1"
}

_print_balances() {
  miner=$(_balance $1)
  other=$(_balance $2)
  echo "miner: $miner"
  echo "other: $other"
}


export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto/kryolite}
export DOCKER_BUILDKIT=1

if [[ $(arch) == "arm64" ]]
then
  RUNTIME=linux-arm64
  VARIANT=linux-arm64
else
  RUNTIME=linux-x64
  VARIANT=linux-x64
fi
export RUNTIME
export VARIANT

_cleanup

docker-compose -f docker-compose.builder.yml up -d --force-recreate daemon kryolite miner

wallet_miner=$(docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite wallet create)
wallet_other=$(docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite wallet create)
echo "wallet_miner: $wallet_miner"
echo "wallet_other: $wallet_other"

(
  exec docker-compose -f docker-compose.builder.yml exec -T miner kryolite-miner --url http://daemon:5000 --address "$wallet_miner"
) 2>&1 | sed -le "s#^#miner: #;" &

while true
do
  miner=$(_balance $wallet_miner)
  if [[ $miner -gt 0 ]]
  then
    echo "miner got balance: $miner"
    break
  fi

  echo "miner balance: $miner"
  sleep 1
done

docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite send --node http://daemon:5000 --from "$wallet_miner" --to "$wallet_other" --amount 1
echo "sent 1 from miner to other"

while true
do
  other=$(_balance $wallet_other)
  if [[ $other -gt 0 ]]
  then
    echo "other got balance: $other"
    if [[ "$other" != 1000000 ]]; then
      echo "unexpected balance"
      exit 1
    fi

    break
  fi

  echo "other balance: $other"
  sleep 1
done

docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite send --node http://daemon:5000 --from "$wallet_miner" --to "$wallet_other" --amount 1
echo "sent 1 from miner to other"

while true
do
  other=$(_balance $wallet_other)
  if [[ $other -gt 1000000 ]]
  then
    echo "other got balance: $other"
    if [[ "$other" != 2000000 ]]; then
      echo "unexpected balance"
      exit 1
    fi

    break
  fi

  echo "other balance: $other"
  sleep 1
done

docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite send --node http://daemon:5000 --from "$wallet_other" --to "$wallet_miner" --amount 1
echo "sent 1 from other to miner"

while true
do
  other=$(_balance $wallet_other)
  if [[ $other -lt 2000000 ]]
  then
    echo "other got balance: $other"
    if [[ "$other" != 999999 ]]; then
      echo "unexpected balance"
      exit 1
    fi

    break
  fi

  echo "other balance: $other"
  sleep 1
done



_cleanup
echo "TEST OK"
