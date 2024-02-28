
using ServiceModel.Grpc.DesignTime;

namespace Kryolite.Grpc.DataService;

[ImportGrpcService(typeof(IDataService))]
[ExportGrpcService(typeof(IDataService), GenerateAspNetExtensions = true)]
public static partial class GrpcDataServices
{

}
