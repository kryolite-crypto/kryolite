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

COMPONENT=$1
RUNTIME=$2
DIST=$3

build=$(mktemp -d)
dotnet publish -c Release -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained --runtime="$RUNTIME" -o "$build" "$COMPONENT"

target=$(mktemp -d)
case "$RUNTIME" in
  win-*)
    cp "$build/$COMPONENT.exe" "$target"
  ;;
  *)
    cp "$build/$COMPONENT" "$target"
  ;;
esac


zip -jpr "${DIST}/${COMPONENT}-${RUNTIME}.zip" "$target"
