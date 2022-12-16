FROM ubuntu:22.04
ENV DOTNET_ROOT=/root/.dotnet
ENV PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools

RUN apt-get update && apt-get install -y curl

RUN mkdir /ghjk && cd /ghjk \
  && curl -Lsf https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh \
  && chmod +x ./dotnet-install.sh \
  && ./dotnet-install.sh --channel 7.0 \
  && rm -rf /ghjk

RUN apt-get update && apt-get install -y libicu-dev zip

WORKDIR /src

COPY shared shared
COPY node node

COPY daemon daemon
COPY louhi-miner louhi-miner
COPY holvi-wallet holvi-wallet

COPY builder.sh .

ENTRYPOINT [ "/src/builder.sh" ]
