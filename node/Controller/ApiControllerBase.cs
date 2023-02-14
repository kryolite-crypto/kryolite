using System.ComponentModel.DataAnnotations;
using Kryolite.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kryolite.Node;

[ApiController]
public class ApiControllerBase : Controller
{
    private readonly IBlockchainManager blockchainManager;
    private readonly INetworkManager networkManager;
    private readonly IMeshNetwork meshNetwork;

    public ApiControllerBase(IBlockchainManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork)
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
    public ulong GetBalance([BindRequired, FromQuery] string wallet)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid parameter (address)");
        }

        return blockchainManager.GetBalance(wallet);
    }

    [HttpGet("peers")]
    public List<string> GetPeers()
    {
        return meshNetwork.GetPeers()
            .Where(x => x.Value.IsReachable)
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

    [HttpGet("block/pos")]
    public PosBlock? GetPosBlock([BindRequired, FromQuery] long height)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid parameter (address)");
        }

        return blockchainManager.GetPosBlock(height);
    }

    [HttpGet("block/pow")]
    public PowBlock? GetPowBlock([BindRequired, FromQuery] long height)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid parameter (address)");
        }

        return blockchainManager.GetPowBlock(height);
    }

    [HttpGet("block/latest")]
    public PosBlock? GetLatestBlock()
    {
        var height = blockchainManager.GetChainState().POS.Height;
        return blockchainManager.GetPosBlock(height);
    }

    [HttpGet("block/latest/pow")]
    public PowBlock? GetLatestPowBlock()
    {
        var height = blockchainManager.GetChainState().POW.Height;
        return blockchainManager.GetPowBlock(height);
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

    [HttpGet("contract/{address}/state")]
    public IActionResult GetSmartContractState(string address)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        var state = blockchainManager.GetContractState(address) ?? string.Empty;
        return Content(state, "application/json");
    }

    [HttpPost("solution")]
    public bool PostSolution([FromBody] Blocktemplate blocktemplate)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid blocktemplate");
        }

        var block = new PowBlock {
            Height = blocktemplate.Height,
            ParentHash = blocktemplate.ParentHash,
            Timestamp = blocktemplate.Timestamp,
            Nonce = blocktemplate.Solution,
            Difficulty = blocktemplate.Difficulty,
            Transactions = blocktemplate.Transactions
        };

        return networkManager.ProposeBlock(block);
    }

    [HttpPost("tx")]
    public void PostTransaction([FromBody] Transaction tx)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid transaction");
        }

        blockchainManager.AddTransactionsToQueue(tx);
    }

    [HttpGet("richlist")]
    public IActionResult GetSmartContractState([FromQuery] int count = 25)
    {
        var wallets = blockchainManager.GetRichList(count).Select(wallet => new {
            Address = wallet.Address,
            Balance = wallet.Balance,
            Pending = wallet.Pending
        });

        return Ok(wallets);
    }

    [HttpGet("tx/{hash}")]
    public IActionResult GetTransactionForHash(string hash)
    {
        return Ok(blockchainManager.GetTransactionForHash(hash));
    }

    [HttpGet("ledger/{address}")]
    public IActionResult GetWalletForAddress(string address)
    {
        if (!Address.IsValid(address))
        {
            return BadRequest();
        }

        return Ok(blockchainManager.GetLedgerWallet(address));
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
}
