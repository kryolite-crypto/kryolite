#!/usr/bin/env bash
set -Eeuo pipefail

echo "-- wallet/build.sh"

case "$RUNTIME" in
  osx-*)
    pushd wallet
    set -x
    dotnet restore -r "$RUNTIME" -p:SelfContained=true
    dotnet msbuild \
      -t:BundleApp -p:Configuration=Release \
      -p:RuntimeIdentifier="$RUNTIME" \
      -p:CFBundleVersion="$INFORMATIONAL_VERSION" -p:CFBundleShortVersionString="$VERSION" \
      -p:UseAppHost=true -p:TargetFramework=net7.0 -p:SelfContained=true -p:PublishSingleFile=true

    cp "bin/Release/net7.0/${RUNTIME}/publish/Kryolite Wallet.app" /build
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
      -o /build \
    set +x
  ;;
esac

case "$RUNTIME" in
  win-*)
    :
  ;;
  osx-*)
    :
  ;;
  *)
    mv /build/wallet /usr/local/bin/kryolite-wallet
    rm -rf /build
  ;;
esac

echo ""
echo "-- wallet/build.sh OK"
