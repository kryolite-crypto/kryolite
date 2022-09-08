using Microsoft.AspNetCore.Mvc;

namespace Marccacoin;

public class ApiController : Controller
{
    private readonly IBlockchainManager blockchainManager;

    public ApiController(IBlockchainManager blockchainManager)
    {
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
    }

    [HttpGet("blocktemplate")]
    public IActionResult GetBlockTemplate([FromQuery]string wallet)
    {
        return Ok();
    }
}