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

COMPONENT=$1
VARIANT=$2
DIST=$3

case "$VARIANT" in
  win-*)
    runtime="$VARIANT"
  ;;
  mac-x64)
    runtime=osx.11.0-x64
  ;;
  mac-arm64)
    runtime=osx.11.0-arm64
  ;;
esac

build=$(mktemp -d)
dotnet publish -c Release -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained --runtime="$runtime" -o "$build" "$COMPONENT"

target=$(mktemp -d)
case "$VARIANT" in
  win-*)
    cp "$build/$COMPONENT.exe" "$target"
  ;;
  *)
    cp "$build/$COMPONENT" "$target"
  ;;
esac


zip -jpr "${DIST}/${COMPONENT}-${VARIANT}.zip" "$target"
