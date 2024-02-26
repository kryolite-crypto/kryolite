using System.Runtime.Serialization;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MemoryPack;

namespace Kryolite.Grpc.NodeService;

[DataContract]
[MemoryPackable]
public partial class ViewResponse
{
    [DataMember]
    public View? View { get; set; }
    [DataMember]
    public List<Block> Blocks { get; set; } = [];
    [DataMember]
    public List<Vote> Votes { get; set; } = [];
    [DataMember]
    public List<TransactionDto> Transactions { get; set; } = [];
}

[DataContract]
[MemoryPackable]
public partial class ViewRangeResponse
{
    [DataMember]
    public List<ViewResponse> Views { get; set; } = new();
}
