#!/usr/bin/env bash
set -Eeuo pipefail

echo "-- wallet/build.sh"

if [[ "${1:-}" != "" ]]
then
  output="$1"
else
  output="/build"
fi

case "$RUNTIME" in
  osx.*)
    pushd wallet
    set -x

    dotnet restore -r "$RUNTIME" -p:SelfContained=true

    dotnet msbuild \
      -t:BundleApp -p:Configuration=Release \
      -p:RuntimeIdentifier="$RUNTIME" \
      -p:CFBundleVersion="$INFORMATIONAL_VERSION" -p:CFBundleShortVersionString="$VERSION" \
      -p:UseAppHost=true -p:TargetFramework=net7.0 -p:SelfContained=true -p:PublishSingleFile=true

    pushd "bin/Release/net7.0/${RUNTIME}/publish"

    zip -pr "${output}/kryolite-wallet.zip" "Kryolite Wallet.app"

    popd

    # save docker layer space before somebody figures out -o flag for msbuild
    rm -rf bin obj

    set +x
    popd
  ;;
  *)
    set -x

    dotnet publish wallet \
      -c Release \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:Version="${VERSION}" -p:InformationalVersion="${INFORMATIONAL_VERSION}" \
      --self-contained --runtime="$RUNTIME" \
      -o "${output}"

    set +x
  ;;
esac

case "$RUNTIME" in
  win-*)
    :
  ;;
  osx.*)
    :
  ;;
  *)
    mv "${output}/wallet" /usr/local/bin/kryolite-wallet
    rm -rf "${output}"
  ;;
esac

echo ""
echo "-- wallet/build.sh OK"
