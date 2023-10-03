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

    [HttpGet("tx/{hash}")]
    public IActionResult GetTransactionForHash(string hash)
    {
        return Ok(blockchainManager.GetTransactionForHash(hash));
    }

    [HttpGet("tx/height/{height}")]
    public IActionResult GetTransactions([FromRoute(Name = "height")] long height)
    {
        var view = blockchainManager.GetView(height);

        if (view is null)
        {
            return Ok(new List<Transaction>());
        }

        return Ok(blockchainManager.GetTransactions(view.Transactions));
    }

    [HttpGet("tx")]
    public IActionResult GetTransactions([FromQuery(Name = "pageNum")] int pageNum = 0, [FromQuery(Name = "pageSize")] int pageSize = 100)
    {
        return Ok(blockchainManager.GetTransactions(pageNum, pageSize));
    }

    [HttpGet("tx/graph")]
    public IActionResult GetTransactionGraph([FromQuery] long startHeight)
    {
        /*var transactions = blockchainManager.GetTransactionsAfterHeight(startHeight);

        var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>(true, transactions.Count + 1);
        var map = new Dictionary<SHA256Hash, Transaction>(transactions.Count + 1);

        var terminatinEdges = new HashSet<SHA256Hash>();

        graph.AddVertexRange(transactions.Select(x => x.TransactionId));

        foreach (var tx in transactions)
        {
            map.Add(tx.TransactionId, tx);
            graph.AddEdge(new (tx., tx.TransactionId ));
        }

        var darkslategray4 = new GraphvizColor(byte.MaxValue, 52, 139, 139);
        var deepskyblue3 = new GraphvizColor(byte.MaxValue, 0, 154, 205);
        var darkslateblue = new GraphvizColor(byte.MaxValue, 48, 61, 139);
        var goldenrod2 = new GraphvizColor(byte.MaxValue, 238, 180, 22);

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
                    if (terminatinEdges.Contains(args.Vertex))
                    {
                        args.VertexFormat.ToolTip = "Not loaded";
                        args.VertexFormat.Shape = GraphvizVertexShape.Point;
                        return;
                    }

                    var tx = map[args.Vertex];

                    args.VertexFormat.Url = $"/explorer/tx/{tx.TransactionId}";

                    switch (tx.TransactionType)
                    {
                        case TransactionType.PAYMENT:
                            args.VertexFormat.ToolTip = $"Transaction";
                            args.VertexFormat.FillColor = darkslateblue;
                            args.VertexFormat.StrokeColor = darkslateblue;
                        break;
                        case TransactionType.GENESIS:
                            args.VertexFormat.ToolTip = $"Genesis";
                            args.VertexFormat.FillColor = GraphvizColor.White;
                            args.VertexFormat.FontColor = GraphvizColor.Black;
                        break;
                        case TransactionType.BLOCK:
                            args.VertexFormat.ToolTip = $"Block";
                            args.VertexFormat.FillColor = goldenrod2;
                            args.VertexFormat.StrokeColor = goldenrod2;
                        break;
                        case TransactionType.VIEW:
                            args.VertexFormat.ToolTip = $"View #{BitConverter.ToInt64(tx.Data)}";
                            args.VertexFormat.FillColor = deepskyblue3;
                            args.VertexFormat.StrokeColor = deepskyblue3;
                        break;
                        case TransactionType.VOTE:
                            args.VertexFormat.ToolTip = $"Vote";
                            args.VertexFormat.FillColor = darkslategray4;
                            args.VertexFormat.StrokeColor = darkslategray4;
                        break;
                        case TransactionType.CONTRACT:
                            args.VertexFormat.ToolTip = $"Contract";
                            args.VertexFormat.FillColor = GraphvizColor.White;
                            args.VertexFormat.FontColor = GraphvizColor.Black;
                        break;
                        case TransactionType.REG_VALIDATOR:
                            args.VertexFormat.ToolTip = $"New Validator";
                            args.VertexFormat.FillColor = GraphvizColor.White;
                            args.VertexFormat.FontColor = GraphvizColor.Black;
                        break;
                    }
                };
            });

        return Ok(dotString);*/
        return Ok("");
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

        return Ok(blockchainManager.GetTransactionsForAddress(address));
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
    public ChainState GetCurrentChainState()
    {
        return blockchainManager.GetChainState() ?? new ChainState();
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
