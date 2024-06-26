using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using Kryolite.Node.Network;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Type;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kryolite.Node.API;

public static class BaseApi
{
    public static IEndpointRouteBuilder RegisterBaseApi(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("blocktemplate", GetBlockTemplate);
        builder.MapGet("block/{hash}", GetBlock);
        builder.MapGet("chainstate", GetCurrentChainState);
        builder.MapGet("chainstate/height/{height}", GetChainstateAtHeight);
        builder.MapGet("contract/{address}", GetSmartContract);
        builder.MapGet("contract/{address}/tokens", GetSmartContractTokens);
        builder.MapGet("data/history", GetHistoryData);
        builder.MapGet("ledger/{address}", GetWalletForAddress);
        builder.MapGet("ledger/{address}/balance", GetBalanceForAddress);
        builder.MapGet("ledger/{address}/transactions", GetTransactionsForAddress);
        builder.MapGet("ledger/{address}/tokens", GetTokensForAddress);
        builder.MapGet("nodes", GetKnownNodes);
        builder.MapGet("peers", GetPeers);
        builder.MapGet("richlist", GetRichList);
        builder.MapGet("token/{contractAddress}/{tokenId}", GetTokenForTokenId);
        builder.MapGet("tx", GetTransactions);
        builder.MapGet("tx/{hash}", GetTransactionForHash);
        builder.MapGet("tx/height/{height}", GetTransactionsAtHeight);
        builder.MapGet("validator", GetValidators);
        builder.MapGet("validator/{address}", GetValidator);
        builder.MapGet("view/{hash}", GetView);
        builder.MapGet("view/height/{height}", GetViewAtHeight);
        builder.MapGet("vote/{hash}", GetVote);

        builder.MapPost("solution", PostSolution);
        builder.MapPost("contract/{address}/call", CallContractMethod);
        builder.MapPost("tx", PostTransaction);
        builder.MapPost("tx/batch", PostTransactions);
        builder.MapPost("tx/fee", EstimateTransactionFee);

        return builder;
    }

    private static Task<BlockTemplate> GetBlockTemplate(IStoreManager storeManager, string wallet, long? id) => Task.Run(() =>
    {
        if (id.HasValue && storeManager.GetCurrentHeight() == id.Value)
        {
            throw new ArgumentException("invalid id");
        }

        return storeManager.GetBlocktemplate(wallet);
    });

    private static Task<IEnumerable<PeerStatsDto>> GetPeers(IConnectionManager connectionManager) => Task.Run(() =>
    {
        var hosts = connectionManager.GetConnectedNodes()
            .Select(x => new PeerStatsDto(x));

        return hosts;
    });

    private static Task<Contract?> GetSmartContract(IStoreManager storeManager, string address) => Task.Run(() =>
    {
        if (!Address.IsValid(address))
        {
            throw new ArgumentException("invalid address");
        }

        return storeManager.GetContract(address);
    });

    private static Task<List<Token>> GetSmartContractTokens(IStoreManager storeManager, string address) => Task.Run(() =>
    {
        if (!Address.IsValid(address))
        {
            throw new ArgumentException("invalid address");
        }

        return storeManager.GetContractTokens(address);
    });

    private static Task<string?> CallContractMethod(HttpContext ctx, IStoreManager storeManager, string address, CallMethod callMethod) => Task.Run(() =>
    {
        if (!Address.IsValid(address))
        {
            throw new ArgumentException("invalid address");
        }

        ctx.Response.ContentType = "application/json";

        return storeManager.CallContractMethod(address, callMethod, out _);
    });

    private static Task<bool> PostSolution(IStoreManager storeManager, BlockTemplate blocktemplate) => Task.Run(() =>
    {
        return storeManager.AddBlock(blocktemplate, true);
    });

    private static Task<TransactionStatusDto> PostTransaction(IStoreManager storeManager, TransactionDto tx) => Task.Run(() =>
    {
        var result = ExecutionResult.UNKNOWN;

        result = tx.TransactionType switch
        {
            TransactionType.PAYMENT or TransactionType.CONTRACT or TransactionType.REGISTER_VALIDATOR or TransactionType.DEREGISTER_VALIDATOR => storeManager.AddTransaction(tx, true),
            _ => throw new Exception("invalid transaction type"),
        };

        if (result != ExecutionResult.PENDING)
        {
            return new TransactionStatusDto(tx.CalculateHash(), result.ToString());
        }

        /*if (wait)
        {
            // wait max 2 minutes for execution
            var expires = DateTime.Now.AddMinutes(2);

            while (expires > DateTime.Now)
            {
                result = storeManager.GetTransactionForHash(tx.CalculateHash())?.ExecutionResult ?? ExecutionResult.UNKNOWN;

                if (result != ExecutionResult.PENDING)
                {
                    break;
                }

                await Task.Delay(1000);
            }
        }*/

        return new TransactionStatusDto(tx.CalculateHash(), result.ToString());
    });

    private static Task<List<TransactionStatusDto>> PostTransactions(IStoreManager storeManager, List<TransactionDto> transactions) => Task.Run<List<TransactionStatusDto>>(() =>
    {
        /*foreach (var tx in transactions)
        {
            storeManager.AddTransactions(tx, true);
        }*/

        return [];
    });

    private static Task<IEnumerable<WalletBalanceDto>> GetRichList(IStoreManager storeManager, int count = 25) => Task.Run(() =>
    {
        return storeManager.GetRichList(count).Select(wallet => new WalletBalanceDto(wallet.Address, wallet.Balance));
    });

    private static Task<ViewDto?> GetView(IStoreManager storeManager, string hash) => Task.Run(() =>
    {
        var view = storeManager.GetView(hash);

        if (view is null)
        {
            return null;
        }

        return new ViewDto(view);
    });

    private static Task<ViewDto?> GetViewAtHeight(IStoreManager storeManager, long height) => Task.Run(() =>
    {
        var view = storeManager.GetView(height);

        if (view is null)
        {
            return null;
        }

        return new ViewDto(view);
    });

    private static Task<BlockDto?> GetBlock(IStoreManager storeManager, string hash) => Task.Run(() =>
    {
        var block = storeManager.GetBlock(hash);

        if (block is null)
        {
            return null;
        }

        return new BlockDto(block);
    });

    private static Task<VoteDto?> GetVote(IStoreManager storeManager, string hash) => Task.Run(() =>
    {
        var vote = storeManager.GetVote(hash);

        if (vote is null)
        {
            return null;
        }

        return new VoteDto(vote);
    });

    private static Task<TransactionDtoEx?> GetTransactionForHash(IStoreManager storeManager, string hash) => Task.Run(() =>
    {
        var tx = storeManager.GetTransactionForHash(hash);

        if (tx is null)
        {
            return null;
        }

        return new TransactionDtoEx(tx);
    });

    private static Task<IEnumerable<TransactionDtoEx>> GetTransactionsAtHeight(IStoreManager storeManager, long height) => Task.Run(() =>
    {
        return storeManager.GetTransactionsAtHeight(height).Select(tx => new TransactionDtoEx(tx));
    });

    private static Task<IEnumerable<TransactionDtoEx>> GetTransactions(IStoreManager storeManager, int pageNum = 0, int pageSize = 100) => Task.Run(() =>
    {
        return storeManager.GetTransactions(pageNum, pageSize).Select(tx => new TransactionDtoEx(tx));
    });

    private static Task<Ledger?> GetWalletForAddress(IStoreManager storeManager, string address) => Task.Run(() =>
    {
        if (!Address.IsValid(address))
        {
            return null;
        }

        return storeManager.GetLedger(address);
    });

    private static Task<ulong> GetBalanceForAddress(IStoreManager storeManager, string address) => Task.Run(() =>
    {
        if (!Address.IsValid(address))
        {
            return 0UL;
        }

        return storeManager.GetBalance(address);
    });

    private static Task<IEnumerable<TransactionDtoEx>> GetTransactionsForAddress(IStoreManager storeManager, string address) => Task.Run(() =>
    {
        if (!Address.IsValid(address))
        {
            return [];
        }

        return storeManager.GetTransactionsForAddress(address).Select(tx => new TransactionDtoEx(tx));
    });

    private static Task<List<Token>> GetTokensForAddress(IStoreManager storeManager, string address) => Task.Run(() =>
    {
        if (!Address.IsValid(address))
        {
            return [];
        }

        return storeManager.GetTokens(address);
    });

    private static Task<Token?> GetTokenForTokenId(IStoreManager storeManager, string contractAddress, string tokenId) => Task.Run(() =>
    {
        return storeManager.GetToken(contractAddress, tokenId);
    });

    private static Task<Validator?> GetValidator(IStoreManager storeManager, string address) => Task.Run(() =>
    {
        return storeManager.GetStake(address);
    });

    private static Task<List<Validator>> GetValidators(IStoreManager storeManager) => Task.Run(() =>
    {
        return storeManager.GetValidators();
    });

    private static ChainStateDto GetCurrentChainState(IStoreManager storeManager)
    {
        var chainState = storeManager.GetChainState();
        return new ChainStateDto(chainState);
    }

    private static ChainStateDto? GetChainstateAtHeight(IStoreManager storeManager, long height)
    {
        var chainState = storeManager.GetChainState(height);

        if (chainState is null)
        {
            return null;
        }

        return new ChainStateDto(chainState);
    }

    private static HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(500)
    };

    private static async Task<IEnumerable<NodeDtoEx>> GetKnownNodes(NodeTable nodeTable)
    {
        var nodes = nodeTable.GetAllNodes();
        var results = new ConcurrentBag<NodeDtoEx>();

        await Parallel.ForEachAsync(nodes, async (node, token) =>
        {
            var hostname = node.Uri.ToHostname();
            ChainStateDto? chainState = null;

            try
            {
                var result = await _httpClient.GetAsync($"{hostname}/chainstate");

                if (result.IsSuccessStatusCode)
                {
                    chainState = JsonSerializer.Deserialize(await result.Content.ReadAsStringAsync(), SharedSourceGenerationContext.Default.ChainStateDto);
                }
            }
            catch (Exception)
            {

            }

            var nodeDto = new NodeDtoEx
            {
                PublicKey = node.PublicKey,
                Url = hostname,
                FirstSeen = node.FirstSeen,
                LastSeen = node.LastSeen,
                Version = node.Version,
                Height = chainState?.Id,
                Weight = chainState?.Weight,
                LastHash = chainState?.LastHash
            };

            results.Add(nodeDto);
        });

        return results.AsEnumerable();
    }

    private static Task<HistoryData> GetHistoryData(IStoreManager storeManager) => Task.Run(() =>
    {
        const int lookback = 120;

        var endState = storeManager.GetChainState();
        var startHeight = Math.Max(0, endState.Id - lookback);
        var startState = storeManager.GetChainState(startHeight);

        var startView = storeManager.GetView(startHeight - 1);
        var prevTimestamp = startView?.Timestamp ?? 0;
        var prevWeight = startState?.Weight ?? BigInteger.Zero;

        var data = new HistoryData();
        data.Difficulty.EnsureCapacity(lookback);
        data.Weight.EnsureCapacity(lookback);
        data.TxPerSecond.EnsureCapacity(lookback);

        for (var i = startHeight; i <= endState.Id; i++)
        {
            var state = storeManager.GetChainState(i);

            if (state is null)
            {
                break;
            }

            var view = storeManager.GetView(i);

            if (view is null)
            {
                break;
            }

            var diff = double.Parse(state.CurrentDifficulty.ToString()!);
            var weight = (double)state.Weight;
            var totalWork = (double)state.TotalWork;
            var weightPerView = (double)(state.Weight - prevWeight);
            var tps = 0d;
            var totalTransactions = state.TotalTransactions;

            if (view.Transactions.Count > 0)
            {
                var delta = (view.Timestamp - prevTimestamp) / 1000d;
                tps = view.Transactions.Count / delta;
            }

            data.Difficulty.Add(new TimeData
            {
                X = i,
                Y = diff
            });

            data.Weight.Add(new TimeData
            {
                X = i,
                Y = weight
            });

            data.TxPerSecond.Add(new TimeData
            {
                X = i,
                Y = tps
            });
            
            data.TotalWork.Add(new TimeData
            {
                X = i,
                Y = totalWork
            });

            data.WeightPerView.Add(new TimeData
            {
                X = i,
                Y = weightPerView
            });

            data.TotalTransactions.Add(new TimeData
            {
                X = i,
                Y = totalTransactions
            });

            prevTimestamp = view.Timestamp;
            prevWeight = state.Weight;
        }

        return data;
    });

    private static Task<ulong> EstimateTransactionFee(IStoreManager storeManager, TransactionDto tx) => Task.Run(() =>
    {
        return storeManager.GetTransactionFeeEstimate(new Transaction(tx));
    });
}
