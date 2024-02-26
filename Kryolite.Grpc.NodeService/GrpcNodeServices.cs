using Kryolite.Grpc.NodeService;
using ServiceModel.Grpc.DesignTime;

namespace Kryolite.Node.Network;

[ImportGrpcService(typeof(INodeService))]
[ExportGrpcService(typeof(INodeService), GenerateAspNetExtensions = true)]
public static partial class GrpcNodeServices
{

}
