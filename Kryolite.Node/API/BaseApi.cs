using System.Text.Json;
using Kryolite.Node.Network;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuikGraph;
using QuikGraph.Graphviz;
using QuikGraph.Graphviz.Dot;

namespace Kryolite.Node.API;

public static class BaseApi
{
    public static IEndpointRouteBuilder RegisterBaseApi(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("blocktemplate", GetBlockTemplate);
        builder.MapGet("block/{hash}", GetBlock);
        builder.MapGet("chainstate", GetCurrentChainState);
        builder.MapGet("contract/{address}", GetSmartContract);
        builder.MapGet("contract/{address}/tokens", GetSmartContractTokens);
        builder.MapGet("ledger/{address}", GetWalletForAddress);
        builder.MapGet("ledger/{address}/balance", GetBalanceForAddress);
        builder.MapGet("ledger/{address}/transactions", GetTransactionsForAddress);
        builder.MapGet("ledger/{address}/tokens", GetTokensForAddress);
        builder.MapGet("nodes", GetKnownNodes);
        builder.MapGet("peers", GetPeers);
        builder.MapGet("richlist", GetRichList);
        builder.MapGet("token/{contractAddress}/{tokenId}", GetTokenForTokenId);
        builder.MapGet("tx", GetTransactions);
        builder.MapGet("tx/graph", GetTransactionGraph);
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

    private static Task<string> GetTransactionGraph(IStoreManager storeManager, long startHeight) => Task.Run(() =>
    {
        var currentHeight = storeManager.GetChainState().Id;

        var types = new Dictionary<SHA256Hash, string>((int)(currentHeight - startHeight));
        var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>(true);

        for (var i = startHeight; i <= currentHeight; i++)
        {
            var view = storeManager.GetView(i);

            if (view is not null)
            {
                var viewHash = view.GetHash();

                graph.AddVertex(viewHash);
                types.TryAdd(viewHash, view.Id % 5 == 0 ? "milestone" : "view");

                bool hasConnection = false;

                var blocks = storeManager.GetBlocks(view.Blocks);

                foreach (var block in blocks)
                {
                    var blockhash = block.GetHash();

                    if (graph.ContainsVertex(block.LastHash))
                    {
                        graph.AddVertex(blockhash);
                        graph.AddEdge(new Edge<SHA256Hash>(block.LastHash, blockhash));
                        graph.AddEdge(new Edge<SHA256Hash>(blockhash, viewHash));
                        types.TryAdd(blockhash, "block");

                        hasConnection = true;
                    }
                }

                var votes = storeManager.GetVotes(view.Votes);

                foreach (var vote in votes)
                {
                    var votehash = vote.GetHash();

                    if (graph.ContainsVertex(vote.ViewHash))
                    {
                        graph.AddVertex(votehash);
                        graph.AddEdge(new Edge<SHA256Hash>(vote.ViewHash, votehash));
                        graph.AddEdge(new Edge<SHA256Hash>(votehash, viewHash));
                        types.TryAdd(votehash, "vote");

                        hasConnection = true;
                    }
                }

                var transactions = storeManager.GetTransactions(view.Transactions);

                foreach (var tx in transactions)
                {
                    var txid = tx.CalculateHash();

                    graph.AddVertex(txid);
                    graph.AddEdge(new Edge<SHA256Hash>(viewHash, txid));
                    types.TryAdd(txid, "tx");
                }

                if (!hasConnection && graph.ContainsVertex(view.LastHash))
                {
                    graph.AddEdge(new Edge<SHA256Hash>(view.LastHash, viewHash));
                }
            }
        }

        var darkslategray1 = new GraphvizColor(byte.MaxValue, 151, 255, 255);
        var darkslategray3 = new GraphvizColor(byte.MaxValue, 121, 205, 205);
        var deepskyblue = new GraphvizColor(byte.MaxValue, 0, 191, 255);
        var deepskyblue2 = new GraphvizColor(byte.MaxValue, 0, 178, 238);
        var deepskyblue3 = new GraphvizColor(byte.MaxValue, 0, 154, 205);
        var darkslateblue = new GraphvizColor(byte.MaxValue, 48, 61, 139);
        var goldenrod2 = new GraphvizColor(byte.MaxValue, 238, 180, 22);
        var floralwhite = new GraphvizColor(byte.MaxValue, 255, 250, 240);

        var dotString = graph.ToGraphviz(algorithm =>
            {
                algorithm.CommonVertexFormat.Shape = GraphvizVertexShape.Point;
                algorithm.CommonVertexFormat.FontColor = GraphvizColor.White;
                algorithm.CommonVertexFormat.Style = GraphvizVertexStyle.Filled;
                algorithm.CommonVertexFormat.Size = new GraphvizSizeF(0.08f, 0.08f);
                algorithm.CommonVertexFormat.FixedSize = true;

                algorithm.CommonEdgeFormat.Length = 1;
                algorithm.CommonEdgeFormat.PenWidth = 0.4;
                algorithm.CommonEdgeFormat.StrokeColor = GraphvizColor.WhiteSmoke;
                algorithm.CommonEdgeFormat.HeadArrow = new GraphvizArrow(GraphvizArrowShape.None);

                algorithm.GraphFormat.BackgroundColor = new GraphvizColor(byte.MaxValue, 25, 25, 25);
                algorithm.GraphFormat.RankDirection = GraphvizRankDirection.LR;

                algorithm.FormatVertex += (sender, args) =>
                {
                    var stype = types[args.Vertex];

                    args.VertexFormat.Url = $"https://testnet-1.kryolite.io/explorer/{stype}/{args.Vertex}";

                    switch (stype)
                    {
                        case "milestone":
                            args.VertexFormat.ToolTip = $"View";
                            args.VertexFormat.FillColor = darkslategray1;
                            args.VertexFormat.StrokeColor = darkslategray1;
                            break;
                        case "view":
                            args.VertexFormat.ToolTip = $"View";
                            args.VertexFormat.FillColor = darkslategray1;
                            args.VertexFormat.StrokeColor = darkslategray1;
                            break;
                        case "block":
                            args.VertexFormat.ToolTip = $"Block";
                            args.VertexFormat.FillColor = goldenrod2;
                            args.VertexFormat.StrokeColor = goldenrod2;
                            break;
                        case "vote":
                            args.VertexFormat.ToolTip = $"Vote";
                            args.VertexFormat.FillColor = deepskyblue;
                            args.VertexFormat.StrokeColor = deepskyblue;
                            break;
                        case "tx":
                            args.VertexFormat.ToolTip = $"Transaction";
                            args.VertexFormat.FillColor = darkslateblue;
                            args.VertexFormat.StrokeColor = darkslateblue;
                            break;
                    }
                };
            });

        return dotString;
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
        var chainState = storeManager.GetChainState() ?? throw new Exception("chainstate not found");

        return new ChainStateDto(chainState);
    }

    private static Task<IEnumerable<NodeDto>> GetKnownNodes(NodeTable nodeTable) => Task.Run(() =>
    {
        return nodeTable.GetAllNodes().Select(x => new NodeDto(x.PublicKey, x.Uri.ToHostname(), x.FirstSeen, x.LastSeen));
    });

    private static Task<ulong> EstimateTransactionFee(IStoreManager storeManager, TransactionDto tx) => Task.Run(() =>
    {
        return storeManager.GetTransactionFeeEstimate(new Transaction(tx));
    });
}
