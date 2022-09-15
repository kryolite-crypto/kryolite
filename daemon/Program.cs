using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Crypto.RIPEMD;
using LiteDB;
using Marccacoin.Shared;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using NSec.Cryptography;

namespace Marccacoin.Daemon;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(@"                                         
 __  __    _    ____   ____ ____    _    
|  \/  |  / \  |  _ \ / ___/ ___|  / \   
| |\/| | / _ \ | |_) | |  | |     / _ \  
| |  | |/ ___ \|  _ <| |__| |___ / ___ \ 
|_|  |_/_/   \_|_| \_\\____\____/_/   \_\                                  
        _   _  ___  ____  _____          
       | \ | |/ _ \|  _ \| ____|         
       |  \| | | | | | | |  _|           
       | |\  | |_| | |_| | |___          
       |_| \_|\___/|____/|_____|         
                                         ");
        Console.ForegroundColor = ConsoleColor.Gray;

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

        Directory.CreateDirectory("data");

        var configuration = new ConfigurationBuilder()
            .AddCommandLine(args)
            .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)
            .Build();

         await WebHost.CreateDefaultBuilder()
            .ConfigureLogging(configure => configure.AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>())
            .UseStartup<Startup>()
            .Build()
            .RunAsync();

            var algorithm = SignatureAlgorithm.Ed25519;

            using var key = NSec.Cryptography.Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving });
            var privKey = key.Export(KeyBlobFormat.RawPrivateKey);
            var pubKey = key.Export(KeyBlobFormat.RawPublicKey);

            Console.WriteLine(pubKey.Length);
            Console.WriteLine(privKey.Length);

            Console.WriteLine("Private Key:");
            Console.WriteLine(BitConverter.ToString(privKey).Replace("-", ""));

            Console.WriteLine("Public Key:");
            Console.WriteLine(BitConverter.ToString(pubKey).Replace("-", ""));

            using var sha256 = SHA256.Create();
            var shaHash = sha256.ComputeHash(pubKey);

            using var ripemd = new RIPEMD160Managed();
            var ripemdHash = ripemd.ComputeHash(shaHash);

            var addressBytes = ripemdHash.ToList();
            addressBytes.Insert(0, (byte)Network.MAIN); // network (161 mainnet, 177 testnet)
            addressBytes.Insert(1, 1); // version

            var ripemdBytes = new List<byte>(addressBytes);
            ripemdBytes.InsertRange(0, Encoding.ASCII.GetBytes("FIM0x"));

            var h1 = sha256.ComputeHash(ripemdBytes.ToArray());
            var h2 = sha256.ComputeHash(h1);

            addressBytes.InsertRange(addressBytes.Count, h2.Take(4)); // checksum

            Console.WriteLine("Wallet Address:");
            Console.WriteLine("FIM0x" + BitConverter.ToString(addressBytes.ToArray()).Replace("-", ""));
            Console.WriteLine("Wallet Length " + addressBytes.Count);
            Console.WriteLine("Addr Length " + addressBytes.Count);

            Console.WriteLine("Valid " + Address.IsValid("FIMx" + BitConverter.ToString(addressBytes.ToArray()).Replace("-", "")));


            var signature = new Signature();
            algorithm.Sign(key, BitConverter.GetBytes(42), signature);
            Console.WriteLine(algorithm.Verify(NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, pubKey.AsSpan(), KeyBlobFormat.RawPublicKey), BitConverter.GetBytes(42), signature));
/*
        var blockchainManager = WebHost .Services.GetService<IBlockchainManager>();

        if (blockchainManager is null) {
            throw new ArgumentNullException("blockchainManager");
        }

        Thread.Sleep(TimeSpan.FromSeconds(1));

        var block = new Block {
            Header = new BlockHeader {
                Id = blockchainManager.GetCurrentHeight(),
                ParentHash = blockchainManager.GetLastBlockhash(),
                RootHash = new SHA256Hash(),
                Timestamp = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(),
                Difficulty = blockchainManager.GetCurrentDifficulty()
            }
        };

        try {
            while(true) {

                using var sha256 = SHA256.Create();
                
                var concat = new Concat
                {
                    Buffer = new byte[64]
                };

                Array.Copy(block.Header.GetHash().Buffer, 0, concat.Buffer, 0, 32);

                var nonce = new byte[32];
                Random rd = new Random();
                rd.NextBytes(nonce);
                Array.Copy(nonce, 0, concat.Buffer, 32, 32);

                var sha256Hash = new SHA256Hash
                {
                    Buffer = sha256.ComputeHash(concat.Buffer)
                };

                var target = block.Header.Difficulty.ToTarget();
                var result = sha256Hash.ToBigInteger();

                if (result.CompareTo(target) <= 0) {
                    if (!checkLeadingZeroBits2(sha256Hash.Buffer, (int)block.Header.Difficulty.b0)) {
                        Console.WriteLine("INVALID");
                        Console.WriteLine($"{block.Header.Difficulty.b0}-{block.Header.Difficulty.b1}-{block.Header.Difficulty.b2}-{block.Header.Difficulty.b3}");
                        Console.WriteLine("target = " + target.ToString("X"));
                        Console.WriteLine("result = " + result.ToString("X"));
                        Console.WriteLine(sha256Hash.Buffer.Length);
                        foreach (var b in sha256Hash.Buffer) {
                            Console.WriteLine(b);
                        }
                        break;
                    }

                    block.Header.Nonce = new Nonce {
                        Buffer = nonce
                    };

                    if(!blockchainManager.AddBlock(block)) {
                        Console.WriteLine("Invalid block");
                        continue;
                    }

                    block  = new Block {
                        Header = new BlockHeader {
                            Id = blockchainManager.GetCurrentHeight(),
                            ParentHash = blockchainManager.GetLastBlockhash(),
                            RootHash = new SHA256Hash(),
                            Timestamp = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(),
                            Difficulty = blockchainManager.GetCurrentDifficulty()
                        }
                    };
                }
            }
        } catch (Exception ex) {
            Console.WriteLine(ex);
        }
    }

    private static bool checkLeadingZeroBits2(byte[] hash, int challengeSize) {
        int challengeBytes = challengeSize / 8;
        int remainingBits = challengeSize - (8 * challengeBytes);
        int remainingValue = 255 >> remainingBits;

        for (int i = 0; i < challengeBytes; i++) {
            if (hash[i] != 0) return false;
        }

        return hash[challengeBytes] <= remainingValue;*/
    }
}