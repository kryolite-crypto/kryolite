# marcca

## Compile and Run

Requires .NET 7 SDK
<https://dotnet.microsoft.com/en-us/download/dotnet/7.0>

### Node

```console
cd node
dotnet run
```

### Holvi Wallet

Wallet hosts full Node to connect and synchronize with network

```console
cd holvi-wallet
dotnet run
```

### Louhi Miner

```console
cd louhi-miner
dotnet run --url http://localhost:5000 --address FIM0x000000
```

## docker

```console
docker-compose up --build --force-recreate daemon-builder
```

```console
docker-compose up --build --force-recreate daemon
```

```console
docker-compose up --build --force-recreate --scale daemon=3 daemons
```

```console
docker-compose up --build --force-recreate louhi-miner
```

Get a shell

```console
docker-compose exec daemon bash
```
