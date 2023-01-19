using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Kryolite.Shared;
using Newtonsoft.Json;
using System.CommandLine;

Blocktemplate current = new Blocktemplate();
var tokenSource = new CancellationTokenSource();

var rootCmd = new RootCommand("Kryolite Miner");

var nodeOption = new Option<string>(name: "--url", description: "Node url", getDefaultValue: () => "http://localhost:5000");
rootCmd.AddGlobalOption(nodeOption);

var walletOption = new Option<string>(name: "--address", description: "Wallet address");
rootCmd.AddGlobalOption(walletOption);

var throttleOption = new Option<int?>(name: "--throttle", description: "Milliseconds to sleep between hashes");
rootCmd.AddGlobalOption(throttleOption);

rootCmd.SetHandler(async (url, address, throttle) => { 
    Console.WriteLine($"Connectiong to {url}");

    while (true) {
        var httpClient = new HttpClient();

        var request = await httpClient.GetAsync($"{url}/blocktemplate?wallet={address}");

        request.EnsureSuccessStatusCode();

        var json = await request.Content.ReadAsStringAsync();
        var blocktemplate = JsonConvert.DeserializeObject<Blocktemplate>(json);

        if (blocktemplate == null || blocktemplate.ParentHash == current.ParentHash) {
            Thread.Sleep(TimeSpan.FromSeconds(1));
            continue;
        }

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

                var sha256Hash = (SHA256Hash)sha256.ComputeHash(concat.Buffer);
                var result = sha256Hash.ToBigInteger();

                if (result.CompareTo(target) <= 0) {
                    Console.WriteLine($"{DateTime.Now}: Block found");
                    var solution = current;
                    var bytes = new byte[32];
                    Array.Copy(concat.Buffer, 32, bytes, 0, 32);

                    solution.Solution = bytes;

                    var json = JsonConvert.SerializeObject(blocktemplate);
    
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await httpClient.PostAsync($"{url}/solution", content);

                    break;
                }

                if (throttle is not null) {
                    Thread.Sleep(throttle.Value);
                }
            }
        }).UnsafeStart();

        Thread.Sleep(TimeSpan.FromSeconds(1));
    }
}, nodeOption, walletOption, throttleOption);

await rootCmd.InvokeAsync(args);