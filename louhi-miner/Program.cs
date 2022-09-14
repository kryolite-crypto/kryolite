using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Marccacoin.Shared;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

IConfiguration configuration = new ConfigurationBuilder()
    .AddCommandLine(args)
    .Build();

Blocktemplate current = new Blocktemplate();
var tokenSource = new CancellationTokenSource();

while (true) {
    var httpClient = new HttpClient();

    var request = await httpClient.GetAsync($"{configuration["url"]}/blocktemplate?wallet={configuration["address"]}");

    request.EnsureSuccessStatusCode();

    var json = await request.Content.ReadAsStringAsync();
    var blocktemplate = JsonConvert.DeserializeObject<Blocktemplate>(json);

    if (blocktemplate == null || blocktemplate.Id == current.Id) {
        Thread.Sleep(TimeSpan.FromSeconds(1));
        continue;
    }

    current = blocktemplate;

    Console.WriteLine($"New Block {blocktemplate.Id}, diff = {BigInteger.Log(blocktemplate.Difficulty.ToWork(), 2)}");

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
                Console.WriteLine("Block found");
                var solution = current;
                var bytes = new byte[32];
                Array.Copy(concat.Buffer, 32, bytes, 0, 32);

                solution.Solution = bytes;

                var json = JsonConvert.SerializeObject(blocktemplate);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                await httpClient.PostAsync($"{configuration["url"]}/solution", content);

                break;
            }
        }
    }).UnsafeStart();

    Thread.Sleep(TimeSpan.FromSeconds(1));
}
