using System.Security.Cryptography;
using LiteDB;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Marccacoin;

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
            serialize: (diff) => diff.Value.ToString(),
            deserialize: (bson) => new Difficulty { Value = (uint)bson.AsInt32 }
        );

        var configuration = new ConfigurationBuilder()
            .AddCommandLine(args)
            .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)
            .Build();

         await WebHost.CreateDefaultBuilder()
            .ConfigureLogging(configure => configure.AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>())
            .UseStartup<Startup>()
            .Build()
            .RunAsync();
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