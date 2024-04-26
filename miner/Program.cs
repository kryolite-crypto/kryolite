using System.Security.Cryptography;
using Kryolite.Shared;
using System.CommandLine;
using System.Diagnostics;
using System.CommandLine.Parsing;
using System.Collections.Concurrent;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using System.Text;
using System.Text.Json;
using Kryolite.Shared.Algorithm;

namespace Kryolite.Miner;

public class Program
{
    private static readonly CancellationTokenSource StoppingSource = new();
    private static CancellationTokenSource TokenSource = CancellationTokenSource.CreateLinkedTokenSource(StoppingSource.Token);

    public static async Task<int> Main(string[] args)
    {
        Console.CancelKeyPress += (s, e) =>
        {
            StoppingSource.Cancel();
            TokenSource.Cancel();
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

        rootCmd.SetHandler(async (node, address, throttle, threads) =>
        {
            var url = node ?? await ZeroConf.DiscoverNodeAsync(5) ?? string.Empty;

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

            Console.WriteLine($"Address\t\t{address}");
            Console.WriteLine("Algorithm\tArgon2id");
            Console.WriteLine($"Threads\t\t{threads}");

            Console.WriteLine($"Connecting to {url}");

            var client = new HttpClient()
            {
                BaseAddress = new Uri(url)
            };

            var hashes = 0UL;
            var blockhashes = 0UL;
            var sw = Stopwatch.StartNew();

            var timer = new System.Timers.Timer(TimeSpan.FromMinutes(2))
            {
                AutoReset = true
            };

            timer.Elapsed += (sender, e) =>
            {
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

                new Thread(() =>
                {
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

                                var sha256Hash = Argon2.Hash(concat);
                                var result = sha256Hash.ToBigInteger();

                                if (result.CompareTo(target) <= 0)
                                {
                                    var timespent = DateTime.Now - start;

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

                                    var payload = JsonSerializer.Serialize(solution, SharedSourceGenerationContext.Default.BlockTemplate);
                                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");

                                    var task = client.PostAsync("solution", content, token);
                                    task.Wait(token);

                                    if (timespent.TotalSeconds > 0)
                                    {
                                        Console.WriteLine("{0}: [{1}] Block found! {2:N2} h/s", 
                                            DateTime.Now,
                                            task.Result.IsSuccessStatusCode ? "SUCCESS" : "FAILED",
                                            blockhashes / timespent.TotalSeconds
                                        );
                                    }
                                    else
                                    {
                                        Console.WriteLine("{0}: [{1}] Block found!", 
                                            DateTime.Now,
                                            task.Result.IsSuccessStatusCode ? "SUCCESS" : "FAILED"
                                        );
                                        Console.WriteLine(task.Result.ReasonPhrase);
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
                var token = StoppingSource.Token;
                using var streamReader = new StreamReader(await client.GetStreamAsync($"blocktemplate/{address}/listen", token));

                while (!token.IsCancellationRequested)
                {
                    var message = await streamReader.ReadLineAsync(token);

                    if (message is null)
                    {
                        // Stream ended
                        return;
                    }

                    var blocktemplate = JsonSerializer.Deserialize(message.Replace("data: ", string.Empty), SharedSourceGenerationContext.Default.BlockTemplate);

                    if (blocktemplate is null)
                    {
                        Console.WriteLine("Received invalid blocktemplate from node...");
                        return;
                    }

                    Console.WriteLine($"{DateTime.Now}: New job #{blocktemplate.Height}, diff = {blocktemplate.Difficulty}");

                    var source = TokenSource;
                    TokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                    source.Cancel();
                    source.Dispose();

                    foreach (var job in jobQueue)
                    {
                        job.Add(blocktemplate, token);
                    }

                    // Message ends with \n\n so read the extra line ending
                    await streamReader.ReadLineAsync(token);
                }

                Console.WriteLine("{0}: Disconnected", DateTime.Now);
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
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
