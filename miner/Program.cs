using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Kryolite.Shared;
using System.CommandLine;
using Zeroconf;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using System.Diagnostics;
using System.Timers;

var sw = Stopwatch.StartNew();
var hashes = 1_000;

for (int i = 0; i < hashes; i++)
{
    var test = new byte[64];
    var concat = new Concat()
    {
        Buffer = test
    };

    Random.Shared.NextBytes(test);

    var result = KryoBWT.Hash(concat);
}

sw.Stop();

Console.WriteLine($"Run took {sw.Elapsed.TotalSeconds}");
Console.WriteLine($"Hashrate {hashes / sw.Elapsed.TotalSeconds}");

Console.ReadKey();

var serializerOpts = new JsonSerializerOptions();
serializerOpts.PropertyNameCaseInsensitive = true;
serializerOpts.Converters.Add(new AddressConverter());
serializerOpts.Converters.Add(new NonceConverter());
serializerOpts.Converters.Add(new PrivateKeyConverter());
serializerOpts.Converters.Add(new PublicKeyConverter());
serializerOpts.Converters.Add(new SHA256HashConverter());
serializerOpts.Converters.Add(new SignatureConverter());
serializerOpts.Converters.Add(new DifficultyConverter());

Blocktemplate current = new Blocktemplate();
var tokenSource = new CancellationTokenSource();

var rootCmd = new RootCommand("Kryolite Miner");

var nodeOption = new Option<string?>(name: "--url", description: "Node url");
rootCmd.AddGlobalOption(nodeOption);

var walletOption = new Option<string>(name: "--address", description: "Wallet address");
rootCmd.AddGlobalOption(walletOption);

var throttleOption = new Option<int?>(name: "--throttle", description: "Milliseconds to sleep between hashes");
rootCmd.AddGlobalOption(throttleOption);

rootCmd.SetHandler(async (node, address, throttle) => {
    var url = node ?? await ZeroConf.DiscoverNodeAsync();

    if (node == null && url == null)
    {
        Console.WriteLine("Failed to discover Kryolite node, specify --url parameter");
        return;
    }

    if (address == null || !Address.IsValid(address))
    {
        Console.WriteLine("Invalid address");
        return;
    }

    Console.WriteLine($"Connecting to {url}");

    bool restart = false;
    var attempts = 0;

    var hashes = 0UL;
    var sw = Stopwatch.StartNew();

    var timer = new System.Timers.Timer(TimeSpan.FromMinutes(2));
    timer.AutoReset = true;
    timer.Elapsed += (object? sender, ElapsedEventArgs e) => {
        if (sw.Elapsed.TotalSeconds > 0)
        {
            Console.WriteLine($"Hashrate (1T): {hashes / sw.Elapsed.TotalSeconds}");
        }
    };
    timer.Start();

    while (true) {
        var httpClient = new HttpClient();

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
        catch (Exception) {}

        if (request == null || !request.IsSuccessStatusCode) 
        {
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
        var blocktemplate = JsonSerializer.Deserialize<Blocktemplate>(json, serializerOpts);

        if (!restart && (blocktemplate == null || blocktemplate.ParentHash == current.ParentHash)) 
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));
            continue;
        }

        restart = false;
        current = blocktemplate;

        Console.WriteLine($"{DateTime.Now}: New job {blocktemplate.Height}, diff = {BigInteger.Log(blocktemplate.Difficulty.ToWork(), 2)}");

        tokenSource.Cancel();
        tokenSource = new CancellationTokenSource();
        
        new Thread(async () => {
            var token = tokenSource.Token;

            using var sha256 = SHA256.Create();
            Random rd = new Random();

            var nonce = new byte[32];
            var concat = new Concat
            {
                Buffer = new byte[64]
            };

            Array.Copy(current.Nonce, 0, concat.Buffer, 0, 32);

            var target = current.Difficulty.ToTarget();

            while (!token.IsCancellationRequested) {
                rd.NextBytes(nonce);
                Array.Copy(nonce, 0, concat.Buffer, 32, 32);

                var sha256Hash = KryoBWT.Hash(concat);
                hashes++;
                var result = sha256Hash.ToBigInteger();

                if (result.CompareTo(target) <= 0) {
                    Console.WriteLine($"{DateTime.Now}: Block found");
                    var solution = current;
                    var bytes = new byte[32];
                    Array.Copy(concat.Buffer, 32, bytes, 0, 32);

                    solution.Solution = bytes;

                    var json = JsonSerializer.Serialize(blocktemplate, serializerOpts);
    
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

                    // solution found, wait for new job from node
                    tokenSource.Cancel();
                }

                if (throttle is not null) {
                    Thread.Sleep(throttle.Value);
                }
            }
        }).UnsafeStart();

        Thread.Sleep(TimeSpan.FromSeconds(1));
    }
}, nodeOption, walletOption, throttleOption);

return await rootCmd.InvokeAsync(args);