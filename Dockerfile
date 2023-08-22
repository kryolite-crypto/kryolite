FROM ubuntu:22.04
ENV DOTNET_ROOT=/root/.dotnet
ENV PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
ENV DISPLAY=:0

RUN DEBIAN_FRONTEND=noninteractive \
  apt-get update && apt-get install -y \
  tini \
  curl nano iputils-ping htop \
  xvfb x11vnc netcat fluxbox \
  libicu-dev \
  libc6-dev libgflags-dev libsnappy-dev liblz4-dev libzstd-dev bzip2 lz4 librocksdb-dev

RUN mkdir /ghjk && cd /ghjk \
  && curl -Lsf https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh \
  && chmod +x ./dotnet-install.sh \
  && ./dotnet-install.sh --channel 7.0 \
  && rm -rf /ghjk

RUN [ "$(uname -m)" = "aarch64" ] && arch=arm64 || arch=amd64 \
  && mkdir /ghjk && cd /ghjk \
  && curl -o reflex_linux_${arch}.tar.gz -Lsf "https://github.com/cespare/reflex/releases/download/v0.3.1/reflex_linux_${arch}.tar.gz" \
  && tar -xvof "reflex_linux_${arch}.tar.gz" \
  && mv "reflex_linux_${arch}/reflex" /usr/local/bin \
  && rm -rf /ghjk

WORKDIR /src

ENTRYPOINT [ "/usr/bin/tini", "tail", "--", "-f", "/dev/null" ]
