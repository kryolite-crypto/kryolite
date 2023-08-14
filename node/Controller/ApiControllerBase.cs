using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kryolite.Node;

[ApiController]
public class ApiControllerBase : Controller
{
    private readonly IStoreManager blockchainManager;
    private readonly INetworkManager networkManager;
    private readonly IMeshNetwork meshNetwork;

    public IBufferService<TransactionDto, IncomingTransactionService> TxBuffer { get; }

    public ApiControllerBase(IStoreManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork, IBufferService<TransactionDto, IncomingTransactionService> txBuffer)
    {
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.meshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        TxBuffer = txBuffer ?? throw new ArgumentNullException(nameof(txBuffer));
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
    public bool PostSolution([FromBody] Blocktemplate blocktemplate)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid blocktemplate");
        }

        return blockchainManager.AddBlock(blocktemplate, true);
    }

    [HttpPost("tx")]
    public async Task PostTransaction([FromBody] TransactionDto tx)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid transaction");
        }

        await TxBuffer.AddAsync(tx);
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

    [HttpGet("tx")]
    public IActionResult GetTransactionAfterHeight(long height)
    {
        return Ok(blockchainManager.GetTransactionsAfterHeight(height));
    }

    [HttpGet("chain/tip")]
    public IActionResult GetChainTip()
    {
        return Ok(blockchainManager.GetTransactionToValidate());
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
}
