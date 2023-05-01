using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class PacketContext
{
    public IBlockchainManager BlockchainManager { get; }
    public INetworkManager NetworkManager { get; }
    public IMeshNetwork MeshNetwork { get; }
    public IConfiguration Configuration { get; }
    public ILogger Logger { get; }
    public BufferBlock<Chain> SyncBuffer { get; }
    public BufferBlock<HeartbeatSignature> HeartbeatSignatureBuffer { get; }

    public PacketContext(IBlockchainManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork, IConfiguration configuration, ILogger logger, BufferBlock<Chain> syncBuffer, BufferBlock<HeartbeatSignature> heartbeatSignatureBuffer)
    {
        BlockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        MeshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SyncBuffer = syncBuffer ?? throw new ArgumentNullException(nameof(syncBuffer));
        HeartbeatSignatureBuffer = heartbeatSignatureBuffer ?? throw new ArgumentNullException(nameof(heartbeatSignatureBuffer));
    }
}
