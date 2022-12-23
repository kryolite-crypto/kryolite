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
  exit 1
}
trap '_on_error $?' ERR

export COMPONENT=$1
export TAGS=${*:2}

if [[ "$TAGS" == "" ]]
then
  echo "ERR: no TAGS"
  exit 1
fi

export GITHUB_REPOSITORY=${GITHUB_REPOSITORY:-kryolite-crypto/kryolite}
export DOCKER_BUILDKIT=1

docker pull "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-linux-x64"
docker pull "ghcr.io/${GITHUB_REPOSITORY}/builder:${COMPONENT}-linux-arm64"

docker pull "ghcr.io/${GITHUB_REPOSITORY}/${COMPONENT}:latest" || true

for tag in $TAGS
do
  docker buildx build \
    -f "Dockerfile.release.${COMPONENT}" \
    --build-arg "BUILDKIT_INLINE_CACHE=1" \
    --build-arg "GITHUB_REPOSITORY=${GITHUB_REPOSITORY}" \
    --cache-from "ghcr.io/${GITHUB_REPOSITORY}/${COMPONENT}:latest" \
    --platform linux/arm64,linux/amd64 \
    --push \
    -t "ghcr.io/${GITHUB_REPOSITORY}/${COMPONENT}:${tag}" .

  echo "PUSHED ghcr.io/${GITHUB_REPOSITORY}/${COMPONENT}:${tag}"
done

echo
echo "DOCKER ${COMPONENT} OK"
