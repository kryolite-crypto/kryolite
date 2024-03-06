using System.Security.Cryptography;
using Kryolite.Shared;
using System.CommandLine;
using System.Diagnostics;
using System.CommandLine.Parsing;
using System.Collections.Concurrent;
using Kryolite.Shared.Blockchain;
using ServiceModel.Grpc.Client;
using ServiceModel.Grpc.Configuration;
using Kryolite.Grpc.DataService;
using Grpc.Net.Client;
using Kryolite.Shared.Dto;
using Grpc.Core;
using Kryolite.Grpc.Marshaller;

namespace Kryolite.Miner;

public class Program
{
    private static ManualResetEvent Pause = new ManualResetEvent(false);
    
    private static CancellationTokenSource StoppingSource = new CancellationTokenSource();
    private static CancellationTokenSource TokenSource = CancellationTokenSource.CreateLinkedTokenSource(StoppingSource.Token);

    public static async Task<int> Main(string[] args)
    {
        Console.CancelKeyPress += (s, e) =>
        {
            StoppingSource.Cancel();
            e.Cancel = true;
        };

        var rootCmd = new RootCommand("Kryolite Miner");

        var nodeOption = new Option<string?>(name: "--url", description: "Node url");
        rootCmd.AddGlobalOption(nodeOption);

        var walletOption = new Option<string>(name: "--address", description: "Wallet address");
        rootCmd.AddGlobalOption(walletOption);

        var throttleOption = new Option<int?>(name: "--throttle", description: "Milliseconds to sleep between hashes");
        rootCmd.AddGlobalOption(throttleOption);

        var threadsOption = new Option<int>(name: "--threads", description: "Thread count", getDefaultValue: () => 1);
        rootCmd.AddGlobalOption(threadsOption);

        rootCmd.SetHandler(async (node, address, throttle, threads) => {
            var url = (node ?? await ZeroConf.DiscoverNodeAsync(5) ?? string.Empty).Trim('/');

            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("Failed to discover Kryolite node, use --url parameter");
                return;
            }

            if (address == null || !Address.IsValid(address))
            {
                Console.WriteLine("Invalid --address");
                return;
            }

            Serializer.RegisterTypeResolver(e => e switch
            {
                SerializerEnum.BLOCKTEMPLATE => new BlockTemplate(),
                _ => throw new ArgumentException()
            });

            var opts = new ServiceModelGrpcClientOptions
            {
                MarshallerFactory = MarshallerFactory.Instance
            };

            var clientFactory = new ClientFactory(opts)
                .AddDataServiceClient();

            var client = clientFactory.CreateClient<IDataService>(GrpcChannel.ForAddress(url, new GrpcChannelOptions
            {
                HttpClient = new HttpClient(new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    KeepAlivePingDelay = TimeSpan.FromSeconds(10),
                    KeepAlivePingTimeout =  TimeSpan.FromSeconds(5)
                })
            }));

            Console.WriteLine($"Address\t\t{address}");
            Console.WriteLine("Algorithm\tGrasshopper");
            Console.WriteLine($"Threads\t\t{threads}");

            Console.WriteLine($"Connecting to {url}");

            var hashes = 0UL;
            var blockhashes = 0UL;
            var sw = Stopwatch.StartNew();

            var timer = new System.Timers.Timer(TimeSpan.FromMinutes(2));
            timer.AutoReset = true;
            timer.Elapsed += (sender, e) => {
                if (sw.Elapsed.TotalSeconds > 0)
                {
                    Console.WriteLine("Hashrate: {0:N2} h/s", hashes / sw.Elapsed.TotalSeconds);
                }
            };
            timer.Start();

            var jobQueue = new List<BlockingCollection<BlockTemplate>>();

            for (var i = 0; i < threads; i++)
            {
                var tid = i;
                var observer = new BlockingCollection<BlockTemplate>();

                jobQueue.Add(observer);

                new Thread(() => {
                    var stoppingToken = StoppingSource.Token;

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            var token = TokenSource.Token;
                            var blocktemplate = observer.Take(token);

                            using var sha256 = SHA256.Create();

                            var concat = new Concat
                            {
                                Buffer = new byte[64]
                            };

                            var nonce = new Span<byte>(concat.Buffer, 32, 32);

                            Array.Copy((byte[])blocktemplate.Nonce, 0, concat.Buffer, 0, 32);

                            var target = blocktemplate.Difficulty.ToTarget();

                            blockhashes = 0;
                            var start = DateTime.Now;

                            while (!token.IsCancellationRequested)
                            {
                                Random.Shared.NextBytes(nonce);

                                var sha256Hash = Grasshopper.Hash(concat);
                                var result = sha256Hash.ToBigInteger();

                                if (result.CompareTo(target) <= 0)
                                {
                                    var timespent = DateTime.Now - start;

                                    if (timespent.TotalSeconds > 0)
                                    {
                                        Console.WriteLine("{0}: Block found! {1:N2} h/s", DateTime.Now, blockhashes / timespent.TotalSeconds);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"{DateTime.Now}: Block found!");
                                    }

                                    var bytes = new byte[32];
                                    Array.Copy(concat.Buffer, 32, bytes, 0, 32);

                                    var solution = new BlockTemplate
                                    {
                                        Height = blocktemplate.Height,
                                        To = blocktemplate.To,
                                        Difficulty = blocktemplate.Difficulty,
                                        Nonce = blocktemplate.Nonce,
                                        Solution = bytes,
                                        Timestamp = blocktemplate.Timestamp,
                                        ParentHash = blocktemplate.ParentHash,
                                        Value = blocktemplate.Value
                                    };

                                    var res = client.PostSolution(solution);

                                    if (!res)
                                    {
                                        break;
                                    }
                                }

                                Interlocked.Increment(ref hashes);
                                Interlocked.Increment(ref blockhashes);

                                if (throttle is not null)
                                {
                                    Thread.Sleep(throttle.Value);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("{0} thread {1}: {2}", DateTime.Now, tid, ex.Message);
                        }
                    }
                }).UnsafeStart();
            }

            Console.WriteLine("{0}: {1} thread{2} started", DateTime.Now, threads, threads == 1 ? string.Empty : "s");

            try
            {
                await foreach (var blocktemplate in client.SubscribeToBlockTemplates(address, StoppingSource.Token).WithCancellation(StoppingSource.Token))
                {
                    Console.WriteLine($"{DateTime.Now}: New job #{blocktemplate.Height}, diff = {blocktemplate.Difficulty}");

                    var source = TokenSource;
                    TokenSource = CancellationTokenSource.CreateLinkedTokenSource(StoppingSource.Token);
                    source.Cancel();
                    source.Dispose();

                    foreach (var job in jobQueue)
                    {
                        job.Add(blocktemplate);
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (RpcException)
            {
                Console.WriteLine("{0}: Disconnected", DateTime.Now);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.GetType());
                Console.WriteLine("{0}: {1}", DateTime.Now, ex.Message);
            }
            finally
            {
                if (!StoppingSource.IsCancellationRequested)
                {
                    StoppingSource.Cancel();
                }
            }
        }, nodeOption, walletOption, throttleOption, threadsOption);

        return await rootCmd.InvokeAsync(args);
    }
}
