# kryolite

## Compile and Run

Requires .NET 7 SDK
<https://dotnet.microsoft.com/en-us/download/dotnet/7.0>

### Node

```console
cd node
dotnet run
```

### Wallet

Wallet hosts full Node to connect and synchronize with network

```console
cd wallet
dotnet run
```

### Miner

```console
cd miner
dotnet run --url http://localhost:5001 --address FIM0xA101CFBF69818C624A03AF8C8FDD9B345896EE1215287EABA4CB
```

## docker

Quickstart:

```console
docker-compose up --build --force-recreate --scale daemons=9 --scale miner=3 daemon-builder daemon daemons miner
```

Or:

```console
docker-compose up --build --force-recreate daemon-builder
```

```console
docker-compose up --build --force-recreate daemon
```

```console
docker-compose up --build --force-recreate --scale daemons=3 daemons
```

```console
docker-compose up --build --force-recreate --scale miner=3 miner
```

```console
docker-compose up --build --force-recreate wallet
```

```console
docker-compose up --build --force-recreate cli
```

Get a shell

```console
docker-compose exec daemon bash
```

## Issues

- qemu bake fails <https://github.com/dotnet/dotnet-docker/discussions/3848>
- multi-arch with --load fails <https://github.com/docker/buildx/issues/59#issuecomment-1168619521>
