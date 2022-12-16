using System.Numerics;
using Kryolite.Node;
using Kryolite.Shared;
using LiteDB;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Daemon;

public class Startup
{
    private IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;

        BsonMapper.Global.RegisterType<Difficulty>
        (
            serialize: (diff) => BitConverter.GetBytes(diff.Value),
            deserialize: (bson) => new Difficulty { Value = BitConverter.ToUInt32(bson.AsBinary) }
        );

        BsonMapper.Global.RegisterType<SHA256Hash>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Nonce>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Signature>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Address>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Shared.PublicKey>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Shared.PrivateKey>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<BigInteger>
        (
            serialize: (bigint) => bigint.ToByteArray(),
            deserialize: (bson) => new BigInteger(bson.AsBinary, true)
        );
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlockchainManager, BlockchainManager>()
                .AddSingleton<Lazy<IBlockchainManager>>(c => new Lazy<IBlockchainManager>(c.GetService<IBlockchainManager>()!))
                .AddSingleton<INetworkManager, NetworkManager>()
                .AddSingleton<IMempoolManager, MempoolManager>()
                .AddSingleton<IWalletManager, WalletManager>()
                .AddHostedService<NetworkService>()
                .AddHostedService<BlockchainService>()
                .AddHostedService<MempoolService>()
                .AddHostedService<SampoService>()
                .AddSingleton<StartupSequence>()
                .AddRouting()
                .AddControllers()
                .AddNewtonsoftJson();
    }
}