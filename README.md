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
dotnet run --url http://localhost:5001 --address FIM0xA101CFBF69818C624A03AF8C8FDD9B345896EE1215287EABA4CB
```

## docker

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
docker-compose up --build --force-recreate --scale louhi-miner=3 louhi-miner
```

```console
docker-compose up --build --force-recreate holvi-wallet
```

Get a shell

```console
docker-compose exec daemon bash
```
