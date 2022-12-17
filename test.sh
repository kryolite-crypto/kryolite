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

_cleanup() {
  docker-compose -f docker-compose.builder.yml down --remove-orphans -v -t 0
}

_balance() {
  docker-compose -f docker-compose.builder.yml exec -T daemon curl --silent "http://localhost:5000/balance?wallet=$1"
}

_print_balances() {
  sender=$(_balance $1)
  receiver=$(_balance $2)
  echo "sender: $sender"
  echo "receiver: $receiver"
}


export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto}
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

docker-compose -f docker-compose.builder.yml up --force-recreate -d daemon kryolite miner

wallet_sender=$(docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite wallet create)
wallet_receiver=$(docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite wallet create)
echo "wallet_sender: $wallet_sender"
echo "wallet_receiver: $wallet_receiver"

>/dev/null 2>&1 docker-compose -f docker-compose.builder.yml exec -T miner kryolite-miner --url http://daemon:5000 --address "$wallet_sender" &
echo "started mining"

while true
do
  sender=$(_balance $wallet_sender)
  if [[ $sender -gt 0 ]]
  then
    echo "sender got balance: $sender"
    break
  fi

  echo "sender balance: $sender"
  sleep 1
done

docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite send --node http://daemon:5000 --from "$wallet_sender" --to "$wallet_receiver" --amount 1
echo "sent 1 to receiver"

while true
do
  receiver=$(_balance $wallet_receiver)
  if [[ $receiver -gt 0 ]]
  then
    echo "receiver got balance: $receiver"
    if [[ "$receiver" != 1000000 ]]; then
      echo "unexpected balance"
      exit 1
    fi

    break
  fi

  echo "receiver balance: $receiver"
  sleep 1
done

docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite send --node http://daemon:5000 --from "$wallet_sender" --to "$wallet_receiver" --amount 1
echo "sent 1 to receiver"

while true
do
  receiver=$(_balance $wallet_receiver)
  if [[ $receiver -gt 1000000 ]]
  then
    echo "receiver got balance: $receiver"
    if [[ "$receiver" != 2000000 ]]; then
      echo "unexpected balance"
      exit 1
    fi

    break
  fi

  echo "receiver balance: $receiver"
  sleep 1
done

docker-compose -f docker-compose.builder.yml exec -T kryolite kryolite send --node http://daemon:5000 --from "$wallet_receiver" --to "$wallet_sender" --amount 1
echo "sent 1 to sender"

while true
do
  receiver=$(_balance $wallet_receiver)
  if [[ $receiver -lt 2000000 ]]
  then
    echo "receiver got balance: $receiver"
    if [[ "$receiver" != 999999 ]]; then
      echo "unexpected balance"
      exit 1
    fi

    break
  fi

  echo "receiver balance: $receiver"
  sleep 1
done



_cleanup
echo "TEST OK"
