using System.Runtime.Serialization;

namespace Kryolite.Shared.Dto;

[DataContract]
public partial class NodeDto(PublicKey publicKey, string url, DateTime lastSeen)
{
    [DataMember]
    public PublicKey PublicKey { get; set; } = publicKey;
    [DataMember]
    public string Url { get; set; } = url;
    [DataMember]
    public DateTime LastSeen { get; set; } = lastSeen;
}
