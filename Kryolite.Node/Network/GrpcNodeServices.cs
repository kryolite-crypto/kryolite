using ServiceModel.Grpc.DesignTime;

namespace Kryolite.Node.Network;

[ImportGrpcService(typeof(INodeService))]
[ExportGrpcService(typeof(NodeService), GenerateAspNetExtensions = true)]
public static partial class GrpcNodeServices
{

}
