using System.ComponentModel.DataAnnotations;
using Marccacoin;
using Marccacoin.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Marccacoin;

[ApiController]
public class ApiControllerBase : Controller
{
    private readonly IBlockchainManager blockchainManager;

    public ApiControllerBase(IBlockchainManager blockchainManager)
    {
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
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

    [HttpGet("block")]
    public Block GetBlock([BindRequired, FromQuery] long id)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid parameter (address)");
        }

        return blockchainManager.GetBlock(id);
    }

    [HttpPost("solution")]
    public bool PostSolution([FromBody] Blocktemplate blocktemplate)
    {
        if (!ModelState.IsValid) {
            throw new Exception("invalid blocktemplate");
        }

        var block = new Block {
            Id = blocktemplate.Id,
            Header = new BlockHeader {
                ParentHash = blocktemplate.ParentHash,
                Timestamp = blocktemplate.Timestamp,
                Nonce = blocktemplate.Solution,
                Difficulty = blocktemplate.Difficulty
            },
            Transactions = blocktemplate.Transactions
        };

        return blockchainManager.AddBlock(block);
    }
}
