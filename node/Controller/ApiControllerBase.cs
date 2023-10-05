using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using QuikGraph;
using QuikGraph.Graphviz;
using QuikGraph.Graphviz.Dot;

namespace Kryolite.Node;

[ApiController]
public class ApiControllerBase : Controller
{
    private readonly IStoreManager blockchainManager;
    private readonly INetworkManager networkManager;
    private readonly IMeshNetwork meshNetwork;

    public ApiControllerBase(IStoreManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork)
    {
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.meshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
    }

    [HttpGet("blocktemplate")]
    public Blocktemplate? GetBlockTemplate([BindRequired, FromQuery] string wallet, [FromQuery] long? id)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid parameter (address)");
        }

        if (id.HasValue && blockchainManager.GetCurrentHeight() == id.Value) {
            return null;
        }

        return blockchainManager.GetBlocktemplate(wallet);
    }

    [HttpGet("balance")]
    public long GetBalance([BindRequired, FromQuery] string wallet)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid parameter (address)");
        }

        return blockchainManager.GetBalance(wallet);
    }

    [HttpGet("peers")]
    public List<string> GetPeers()
    {
        return networkManager.GetHosts()
            .Where(x => x.IsReachable)
            .Select(x => x.Url.ToHostname())
            .ToList();
    }

    [HttpGet("peers/connected")]
    public List<string> GetAllPeers()
    {
        return meshNetwork.GetPeers()
            .Select(x => x.Value.Uri.ToHostname())
            .ToList();
    }

    [HttpGet("peers/unreachable")]
    public List<string> GetUnreachablePeers()
    {
        return meshNetwork.GetPeers()
            .Where(x => !x.Value.IsReachable)
            .Select(x => x.Value.Uri.ToHostname())
            .ToList();
    }

    [HttpGet("contract/{address}")]
    public IActionResult GetSmartContract(string address)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        return Ok(blockchainManager.GetContract(address));
    }

    [HttpGet("contract/{address}/tokens")]
    public IActionResult GetSmartContractTokens(string address)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        return Ok(blockchainManager.GetContractTokens(address));
    }

    [HttpPost("contract/{address}/call")]
    public IActionResult CallContractMethod([FromRoute] string address, [FromBody] CallMethod callMethod)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        var json = blockchainManager.CallContractMethod(address, callMethod);

        return Content(json ?? string.Empty, "application/json");
    }

    [HttpPost("solution")]
    public IActionResult PostSolution([FromBody] Blocktemplate blocktemplate)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid blocktemplate");
        }

        var ok = blockchainManager.AddBlock(blocktemplate, true);

        if (!ok)
        {
            return BadRequest();
        }

        return Ok();
    }

    [HttpPost("tx")]
    public async Task<IActionResult> PostTransactionDto([FromBody] TransactionDto tx, [FromQuery]bool wait = false)
    {
        if (!ModelState.IsValid)
        {
            throw new Exception("invalid transaction");
        }
        
        var result = ExecutionResult.UNKNOWN;

        switch (tx.TransactionType)
        {
            case TransactionType.PAYMENT:
                result = blockchainManager.AddTransaction(tx, true);
                break;
            case TransactionType.REG_VALIDATOR:
                result = blockchainManager.AddValidatorReg(tx, true);
                break;
            default:
                throw new Exception("invalid transaction type");
        }
        
        if (result != ExecutionResult.PENDING)
        {
            return Ok(new { TransactionId = tx.CalculateHash(), Status = result.ToString() });
        }
        
        if (wait)
        {
            // wait max 2 minutes for execution
            var expires = DateTime.Now.AddMinutes(2);
            
            while (expires > DateTime.Now)
            {
                result = blockchainManager.GetTransactionForHash(tx.CalculateHash())?.ExecutionResult ?? ExecutionResult.UNKNOWN;
                
                if (result != ExecutionResult.PENDING)
                {
                    break;
                }
                
                await Task.Delay(1000);
            }
        }

        return Ok(new { TransactionId = tx.CalculateHash(), Status = result.ToString() });
    }

    [HttpPost("tx/batch")]
    public bool PostTransactions([FromBody] List<TransactionDto> transactions)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid transaction");
        }

        foreach (var tx in transactions)
        {
            blockchainManager.AddTransaction(tx, true);
        }

        return true;
    }

    [HttpGet("richlist")]
    public IActionResult GetRichList([FromQuery] int count = 25)
    {
        var wallets = blockchainManager.GetRichList(count).Select(wallet => new {
            wallet.Address,
            wallet.Balance
        });

        return Ok(wallets);
    }

    [HttpGet("view/{hash}")]
    public IActionResult GetView(string hash)
    {
        var view = blockchainManager.GetView(hash);

        if (view is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            Id = view.Id,
            Timestamp = view.Timestamp,
            LastHash = view.LastHash,
            PublicKey = view.PublicKey,
            From = view.PublicKey.ToAddress(),
            Signature = view.Signature,
            Transactions = view.Transactions,
            Rewards = view.Rewards,
            Votes = view.Votes,
            Blocks = view.Blocks
        });
    }

    [HttpGet("block/{hash}")]
    public IActionResult GetBlock(string hash)
    {
        var block = blockchainManager.GetBlock(hash);

        if (block is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            To = block.To,
            Value = block.Value,
            Timestamp = block.Timestamp,
            LastHash = block.LastHash,
            Difficulty = block.Difficulty.ToString(),
            Nonce = block.Nonce
        });
    }

    [HttpGet("vote/{hash}")]
    public IActionResult GetVote(string hash)
    {
        var vote = blockchainManager.GetVote(hash);

        if (vote is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            ViewHash = vote.ViewHash,
            PublicKey = vote.PublicKey,
            Address = vote.PublicKey.ToAddress(),
            Signature = vote.Signature
        });
    }

    [HttpGet("tx/{hash}")]
    public IActionResult GetTransactionForHash(string hash)
    {
        var tx = blockchainManager.GetTransactionForHash(hash);

        if (tx is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            TransactionId = tx.CalculateHash(),
            TransactionType = tx.TransactionType,
            From = tx.From,
            To = tx.To,
            Value = tx.Value,
            Timestamp = tx.Timestamp,
            Signature = tx.Signature,
            ExecutionResult = tx.ExecutionResult,
            Effects = tx.Effects
        });
    }

    [HttpGet("tx/height/{height}")]
    public IActionResult GetTransactions([FromRoute(Name = "height")] long height)
    {
        var view = blockchainManager.GetView(height);

        if (view is null)
        {
            return Ok(new List<Transaction>());
        }

        var txs = blockchainManager.GetTransactions(view.Transactions).Select(tx => new
        {
            TransactionId = tx.CalculateHash(),
            TransactionType = tx.TransactionType,
            From = tx.From,
            To = tx.To,
            Value = tx.Value,
            Timestamp = tx.Timestamp,
            Signature = tx.Signature,
            ExecutionResult = tx.ExecutionResult,
            Effects = tx.Effects
        });

        return Ok(txs);
    }

    [HttpGet("tx")]
    public IActionResult GetTransactions([FromQuery(Name = "pageNum")] int pageNum = 0, [FromQuery(Name = "pageSize")] int pageSize = 100)
    {
        var txs = blockchainManager.GetTransactions(pageNum, pageSize).Select(tx => new
        {
            TransactionId = tx.CalculateHash(),
            TransactionType = tx.TransactionType,
            From = tx.From,
            To = tx.To,
            Value = tx.Value,
            Timestamp = tx.Timestamp,
            Signature = tx.Signature,
            ExecutionResult = tx.ExecutionResult,
            Effects = tx.Effects
        });

        return Ok(txs);
    }

    [HttpGet("tx/graph")]
    public IActionResult GetTransactionGraph([FromQuery] long startHeight)
    {
        var currentHeight = blockchainManager.GetChainState().Id;
    
        var types = new Dictionary<SHA256Hash, string>((int)(currentHeight - startHeight));
        var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>(true);

        for (var i = startHeight; i <= currentHeight; i++)
        {
            var view = blockchainManager.GetView(i);

            if (view is not null)
            {
                var viewHash = view.GetHash();

                graph.AddVertex(viewHash);
                types.Add(viewHash, view.Id % 5 == 0 ? "milestone" : "view");

                bool hasConnection = false;

                var blocks = blockchainManager.GetBlocks(view.Blocks);

                foreach (var block in blocks)
                {
                    var blockhash = block.GetHash();

                    if (graph.ContainsVertex(block.LastHash))
                    {
                        graph.AddVertex(blockhash);
                        graph.AddEdge(new Edge<SHA256Hash>(block.LastHash, blockhash));
                        graph.AddEdge(new Edge<SHA256Hash>(blockhash, viewHash));
                        types.Add(blockhash, "block");

                        hasConnection = true;
                    }
                }

                var votes = blockchainManager.GetVotes(view.Votes);

                foreach (var vote in votes)
                {
                    var votehash = vote.GetHash();

                    if (graph.ContainsVertex(vote.ViewHash))
                    {
                        graph.AddVertex(votehash);
                        graph.AddEdge(new Edge<SHA256Hash>(vote.ViewHash, votehash));
                        graph.AddEdge(new Edge<SHA256Hash>(votehash, viewHash));
                        types.Add(votehash, "vote");

                        hasConnection = true;
                    }
                }

                var transactions = blockchainManager.GetTransactions(view.Transactions);

                foreach (var tx in transactions)
                {
                    var txid = tx.CalculateHash();

                    graph.AddVertex(txid);
                    graph.AddEdge(new Edge<SHA256Hash>(viewHash, txid));
                    types.Add(txid, "tx");
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

        return Ok(dotString);
    }

    [HttpGet("ledger/{address}")]
    public IActionResult GetWalletForAddress(string address)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        return Ok(blockchainManager.GetLedger(address));
    }

    [HttpGet("ledger/{address}/balance")]
    public IActionResult GetBalanceForAddress(string address)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        return Ok(blockchainManager.GetBalance(address));
    }

    [HttpGet("ledger/{address}/transactions")]
    public IActionResult GetTransactionsForAddress(string address)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        var txs = blockchainManager.GetTransactionsForAddress(address).Select(tx => new
        {
            TransactionId = tx.CalculateHash(),
            TransactionType = tx.TransactionType,
            From = tx.From,
            To = tx.To,
            Value = tx.Value,
            Timestamp = tx.Timestamp,
            Signature = tx.Signature,
            ExecutionResult = tx.ExecutionResult,
            Effects = tx.Effects
        });

        return Ok(txs);
    }

    [HttpGet("ledger/{address}/tokens")]
    public IActionResult GetTokens(string address)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        return Ok(blockchainManager.GetTokens(address));
    }

    [HttpGet("token/{contractAddress}/{tokenId}")]
    public Token? GetToken(string contractAddress, string tokenId) 
    {
        return blockchainManager.GetToken(contractAddress, tokenId);
    }

    [HttpGet("validator/{address}")]
    public Shared.Validator? GetValidator(string address)
    {
        return blockchainManager.GetStake(address);
    }

    [HttpGet("validator")]
    public List<Shared.Validator> GetValidators()
    {
        return blockchainManager.GetValidators();
    }

    [HttpGet("chainstate")]
    public IActionResult GetCurrentChainState()
    {
        var chainState = blockchainManager.GetChainState();

        return Ok(new
        {
            Id = chainState.Id,
            Weight = chainState.Weight,
            Blocks = chainState.Blocks,
            LastHast = chainState.LastHash,
            CurrentDifficulty = chainState.CurrentDifficulty.ToString(),
            Votes = chainState.Votes,
            Transactions = chainState.Transactions
        });
    }
    
    [HttpGet("nodes")]
    public IActionResult GetKnownNodes()
    {
        var list = networkManager.GetHosts().Select(x => new {
            x.Url,
            x.IsReachable,
            x.LastSeen
        });
        
        return Ok(list);
    }
}
