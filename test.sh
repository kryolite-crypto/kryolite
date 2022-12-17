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

export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto}
export DOCKER_BUILDKIT=1

if [[ $(arch) == "arm64" ]]
then
  RUNTIME=linux-arm64
  VARIANT=linux-arm64
else
  RUNTIME=linux-x64
  VARIANT=linux-arm64
fi
export RUNTIME VARIANT

docker-compose -f docker-compose.builder.yml down --remove-orphans -v -t 0
docker-compose -f docker-compose.builder.yml build base
docker-compose -f docker-compose.builder.yml build daemon miner
docker-compose -f docker-compose.builder.yml up --force-recreate -d daemon kryolite miner

wallet=$(docker-compose -f docker-compose.builder.yml exec kryolite kryolite-wallet create)
echo "wallet: '${wallet}'"
docker-compose -f docker-compose.builder.yml exec miner kryolite-miner --url http://daemon:5000 --address "$wallet"

# until
#   docker-compose -f docker-compose.builder.yml logs --no-log-prefix miner | grep "New job 2"
# do
#   echo "waiting for block to be mined"
# done

# until
#   docker-compose -f docker-compose.builder.yml logs --no-log-prefix daemon | grep "Added block 2"
# do
#   echo "waiting for block to be added"
# done

echo "TEST OK"
