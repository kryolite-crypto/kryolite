#!/usr/bin/env bash

set -eEuo pipefail

TAG=$1

mkdir "kryolite/$TAG"
pushd "kryolite/$TAG"

gh release download --clobber --repo kryolite-crypto/kryolite "$TAG" -p '*-linux-x64.*'

for zip in *.zip
do
  unzip -o "$zip"
done

