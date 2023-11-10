FROM ubuntu:22.04
ENV DOTNET_ROOT=/root/.dotnet
ENV PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools

RUN apt-get update && apt-get install -y \
  curl libicu-dev zip libc6-dev libgflags-dev libsnappy-dev liblz4-dev libzstd-dev bzip2 lz4

RUN mkdir /ghjk && cd /ghjk \
  && curl -Lsf https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh \
  && chmod +x ./dotnet-install.sh \
  && ./dotnet-install.sh --channel 8.0 \
  && rm -rf /ghjk

WORKDIR /build
WORKDIR /src
