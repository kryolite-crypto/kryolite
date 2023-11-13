using System.Security.Cryptography;
using System.Text;
using Kryolite.Shared;
using System.CommandLine;
using System.Net;
using System.Text.Json;
using System.Diagnostics;
using System.CommandLine.Parsing;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Kryolite.Shared.Blockchain;

public class Program
{
    private static JsonSerializerOptions SerializerOpts = new JsonSerializerOptions();
    private static ManualResetEvent Pause = new ManualResetEvent(false);
    private static CancellationTokenSource TokenSource = new CancellationTokenSource();

    public static async Task<int> Main(string[] args)
    {
        SerializerOpts.PropertyNameCaseInsensitive = true;
        SerializerOpts.Converters.Add(new PrivateKeyConverter());
        SerializerOpts.Converters.Add(new PublicKeyConverter());
        SerializerOpts.Converters.Add(new SHA256HashConverter());
        SerializerOpts.Converters.Add(new SignatureConverter());
        SerializerOpts.Converters.Add(new DifficultyConverter());
        SerializerOpts.Converters.Add(new AddressConverter());


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

            var httpClient = new HttpClient();

            var jobQueue = new List<BlockingCollection<Blocktemplate>>();

            for (var i = 0; i < threads; i++)
            {
                var observer = new BlockingCollection<Blocktemplate>();

                jobQueue.Add(observer);
                new Thread(() => {

                    while (true)
                    {
                        var blocktemplate = observer.Take();
                        var token = TokenSource.Token;

                        using var sha256 = SHA256.Create();

                        var concat = new Concat
                        {
                            Buffer = new byte[64]
                        };

                        var nonce = new Span<byte>(concat.Buffer, 32, 32);

                        Array.Copy(blocktemplate.Nonce, 0, concat.Buffer, 0, 32);

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

                                var solution = new Blocktemplate
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

                                _ = Task.Run(async () => {
                                    var json = JsonSerializer.Serialize(solution, SerializerOpts);
                                    using var content = new StringContent(json, Encoding.UTF8, "application/json");

                                    int attempts = 0;

                                    do
                                    {
                                        try
                                        {
                                            var res = await httpClient.PostAsync($"{url}/solution", content);

                                            if (res.IsSuccessStatusCode)
                                            {
                                                break;
                                            }

                                            Console.WriteLine($"Failed to send solution to node (HTTP_ERR = {res.StatusCode}), retry attempt {attempts++}/5");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Failed to send solution to node ({ex.Message}), retry attempt {++attempts}/5");
                                        }

                                    } while (attempts < 5);
                                });
                            }

                            Interlocked.Increment(ref hashes);
                            Interlocked.Increment(ref blockhashes);

                            if (throttle is not null)
                            {
                                Thread.Sleep(throttle.Value);
                            }
                        }
                    }
                }).UnsafeStart();
            }

            await HandleConnectionAsync(jobQueue, httpClient, url!, address);
        }, nodeOption, walletOption, throttleOption, threadsOption);

        return await rootCmd.InvokeAsync(args);
    }

    public static async Task HandleConnectionAsync(List<BlockingCollection<Blocktemplate>> queue, HttpClient httpClient, string url, string address)
    {
        var current = new Blocktemplate();
        var restart = false;
        var attempts = 0;

        while (true)
        {
            HttpResponseMessage? request = null;

            try
            {
                request = await httpClient.GetAsync($"{url}/blocktemplate?wallet={address}");

                if (request.StatusCode == HttpStatusCode.BadRequest)
                {
                    Console.WriteLine($"Failed to fetch blocktemplate (HTTP_ERR = {request.StatusCode}).");
                    return;
                }
            }
            catch (Exception) { }

            if (request == null || !request.IsSuccessStatusCode)
            {
                if (!TokenSource.IsCancellationRequested)
                {
                    TokenSource.Cancel();
                }
                restart = true;
                var seconds = Math.Pow(Math.Min(++attempts, 5), 2);

                Console.WriteLine($"Failed to fetch blocktemplate (HTTP_ERR = {request?.StatusCode ?? HttpStatusCode.RequestTimeout}), trying again in {seconds} seconds");

                var newNode = await ZeroConf.DiscoverNodeAsync();
                if (newNode != null)
                {
                    url = newNode;
                }

                Thread.Sleep(TimeSpan.FromSeconds(seconds));
                continue;
            }

            attempts = 0;

            var json = await request.Content.ReadAsStringAsync();
            var blocktemplate = JsonSerializer.Deserialize<Blocktemplate>(json, SerializerOpts);

            if (!restart && (blocktemplate == null || blocktemplate.ParentHash == current!.ParentHash))
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                continue;
            }

            restart = false;
            current = blocktemplate;

            Console.WriteLine($"{DateTime.Now}: New job #{blocktemplate?.Height}, diff = {blocktemplate?.Difficulty}");

            TokenSource.Cancel();
            TokenSource = new CancellationTokenSource();

            Parallel.ForEach(queue, worker => {
                worker.Add(blocktemplate!);
            });

            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    public static void RunTest(int threads)
    {
        var done = 0;

        var tasks = new List<Thread>();
        var stokenSource = new CancellationTokenSource();

        for (int x = 0; x < threads; x++)
        {
            var t = new Thread(() => {
                var token = stokenSource.Token;
                var test = new byte[64];
                var concat = new Concat()
                {
                    Buffer = test
                };

                while (!token.IsCancellationRequested)
                {
                    Random.Shared.NextBytes(concat.Buffer);

                    var result = Grasshopper.Hash(concat);
                    Interlocked.Increment(ref done);
                }
            });

            tasks.Add(t);
            t.IsBackground = true;
            t.Priority = ThreadPriority.Normal;
        }

        var sw = Stopwatch.StartNew();

        foreach (var task in tasks)
        {
            task.Start();
        }

        Thread.Sleep(TimeSpan.FromSeconds(10));
        stokenSource.Cancel();

        sw.Stop();

        Console.WriteLine($"Run took {sw.Elapsed.TotalSeconds}");
        Console.WriteLine($"Hashrate {done / sw.Elapsed.TotalSeconds}");

        Console.ReadKey();
    }
}
