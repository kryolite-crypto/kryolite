using Kryolite.Grpc.NodeService;
using ServiceModel.Grpc.DesignTime;

namespace Kryolite.Grpc.NodeService;

[ImportGrpcService(typeof(INodeService))]
[ExportGrpcService(typeof(INodeService), GenerateAspNetExtensions = true)]
public static partial class GrpcNodeServices
{

}
